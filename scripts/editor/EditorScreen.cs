using System.Text.RegularExpressions;
using Core;
using Editing;
using Godot;

/// A section listing named entries: where they live in the file, what one is called, the name a new one gets, and
/// what a new one holds.
public record NamedSection(string Key, string Noun, string First, Func<object?> NewValue);

/// One row of a menu: its label, icon, shortcut and what it does, or a submenu.
public record MenuRow(string Label, string Icon = "", Key Shortcut = Key.None, Action? Run = null, PopupMenu? Submenu = null);

/// The game editor, laid out like a code editor: a menu bar; on the left an outline of the game (or the recipes, or
/// the building blocks); in the middle the table (drag zones, or Play), the deck or the game file's text, with the
/// problems below; the selected part's settings on the right (Inspector); and a status bar. Every change edits the
/// game file's text in place (GameDocument), so comments and layout survive; nothing is written until Save.
public partial class EditorScreen : Control
{
    const string MainScene = "res://scenes/main.tscn";
    const string TitleScene = "res://scenes/title.tscn";
    static readonly string[] Sections = ["Game", "Table", "Deck", "Players", "Variables", "Actions", "Turn", "Triggers", "Conditions", "Scoring", "Melds"];
    /// Changes to the same setting within this long count as one step for Undo (e.g. dragging a number).
    const ulong MergeTime = 1500;
    const double ReloadDelay = 0.3;
    const double MessageTime = 5.0;
    const string DecksFolder = "res://decks/";
    const string NewDeck = "name,count\nCard 1,1\n";
    /// Sections listing named entries.
    public static readonly Dictionary<string, NamedSection> Named = new()
    {
        ["Table"] = new("zones", "zone", "new_zone", () => new Dictionary<string, object?> { ["layout"] = "pile", ["anchor"] = new List<object?> { 0.5, 0.5 } }),
        ["Actions"] = new("actions", "action", "New action", () => new Dictionary<string, object?> { ["steps"] = new List<object?>() }),
        ["Triggers"] = new("triggers", "trigger", "new_trigger", () => new List<object?>()),
        ["Conditions"] = new("conditions", "condition", "new_condition", () => "true"),
    };
    /// Which section each part of the file is edited in.
    static readonly Dictionary<string, string> SectionOf = new()
    {
        ["title"] = "Game", ["description"] = "Game", ["zones"] = "Table", ["card_face"] = "Deck", ["variables"] = "Variables",
        ["player_variables"] = "Variables", ["team_variables"] = "Variables", ["constants"] = "Variables", ["actions"] = "Actions", ["turn"] = "Turn",
        ["triggers"] = "Triggers", ["conditions"] = "Conditions", ["players"] = "Players", ["player_names"] = "Players", ["seats"] = "Players",
        ["teams"] = "Players", ["bot"] = "Players", ["status"] = "Scoring", ["show_variables"] = "Scoring", ["melds"] = "Melds",
    };
    internal const int TableTab = 0;
    const int DeckTab = 1;
    internal const int FileTab = 2;
    static readonly (string Title, string Icon)[] Tabs = [("Table", "table"), ("Deck", "deck"), ("Game file", "file")];
    static readonly (string Title, string Icon, string Node)[] SidebarPages = [("Outline", "outline", "OutlinePage"), ("Recipes", "recipes", "RecipesPage"), ("Building blocks", "blocks", "BlocksPage")];

    static BlockHelp? help;

    /// What each building block does, from docs/reference.md.
    public static BlockHelp Help => help ??= new(ProjectFiles.With().Exists(BlockHelp.Reference) ? ProjectFiles.With().Read(BlockHelp.Reference) : "");

    [Export(PropertyHint.File, "*.jsonc,*.json")]
    public string FilePath { get; set; } = "res://games/color_match.jsonc";

    public GameDocument Document { get; private set; } = new();
    public string Section { get; private set; } = "";
    public string Entry { get; private set; } = "";
    /// The game as last checked, for suggesting names and checking steps as they're typed.
    public Game? CheckedGame { get; private set; }

    /// The game file's text and the decks edited here, as one state for undo.
    record EditorState(string Rules, Dictionary<string, string> Decks)
    {
        public bool SameAs(EditorState other) => Rules == other.Rules && Decks.Count == other.Decks.Count && !Decks.Except(other.Decks).Any();
    }

    /// Decks edited here, by path, until saved.
    Dictionary<string, string> deckTexts = [];
    readonly List<EditorState> history = [];
    readonly List<EditorState> future = [];
    EditorState savedState = new("", []);
    List<object> lastChangePath = [];
    ulong lastChangeTime;
    bool playing;
    Main preview = null!;
    ZoneDragger dragger = null!;
    readonly DeckTable deckTable = new();
    readonly CommandPalette palette = new();
    readonly Timer reloadTimer = new() { OneShot = true };
    readonly Timer messageTimer = new() { OneShot = true };
    readonly AcceptDialog recipeAdded = new() { DialogAutowrap = true, MinSize = new Vector2I(440, 0) };
    List<string> problems = [];
    /// The deck shown in the Deck tab.
    string deckShown = "";
    /// The text box (a step, or the game file) last typed in, where building blocks are inserted.
    CodeEdit? lastCode;
    /// Every menu command, for the command palette.
    readonly List<PaletteEntry> commands = [];
    /// The window's scaling before the editor opened, put back when it closes.
    Window.ContentScaleModeEnum scaleMode;
    float scaleFactor;

    Inspector Inspector => GetNode<Inspector>("%Inspector");
    GameOutline Outline => GetNode<GameOutline>("%Outline");
    GameFileView FileView => GetNode<GameFileView>("%FileView");
    TabBar CenterTabs => GetNode<TabBar>("%CenterTabs");
    Tree Problems => GetNode<Tree>("%Problems");
    Control Center => GetNode<Control>("%Center");
    GameFiles Files => ProjectFiles.With(FilesGiven());

    // For tests: the table being edited, how many steps undo can go back, decks edited but not saved, the palette.
    internal Main Preview => preview;
    internal int UndoSteps => history.Count;
    internal int EditedDecks => deckTexts.Count;
    internal CommandPalette Palette => palette;

    public override void _Ready()
    {
        Theme = EditorTheme.Build();
        UseScreenPixels();
        Document = GameDocument.Open(ProjectFiles.With(), FilePath);
        savedState = State();
        Inspector.Setup(this);
        BuildMenus();
        BuildBars();
        BuildSidebar();
        BuildCenter();
        AddChild(recipeAdded);
        AddChild(palette);
        reloadTimer.Timeout += Reload;
        AddChild(reloadTimer);
        messageTimer.Timeout += () => GetNode<Label>("%StatusMessage").Text = "";
        AddChild(messageTimer);
        GetViewport().GuiFocusChanged += control =>
        {
            if (control is CodeEdit code)
            {
                lastCode = code;
            }
        };
        CheckedGame = new(Files, FilePath, 1);
        SelectSection("Table");
        Reload();
        UpdateTitle();
    }

    public override void _ExitTree()
    {
        GetWindow().ContentScaleMode = scaleMode;
        GetWindow().ContentScaleFactor = scaleFactor;
    }

    /// The game scales its table with the window; the editor instead stays at the screen's own size, like any other
    /// application, so a bigger window shows more rather than bigger.
    void UseScreenPixels()
    {
        Window window = GetWindow();
        scaleMode = window.ContentScaleMode;
        scaleFactor = window.ContentScaleFactor;
        window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        window.ContentScaleFactor = Mathf.Max(1, (float)Mathf.Snapped(DisplayServer.ScreenGetDpi() / 96.0, 0.25));
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        // Ctrl+Z and Ctrl+Y are the Edit menu's; this catches Ctrl+Shift+Z.
        if (@event.IsActionPressed("ui_redo"))
        {
            Redo();
        }
    }

    // Building the screen

    static Key Ctrl(Key key) => (Key)KeyModifierMask.MaskCtrl | key;

    static Key CtrlShift(Key key) => (Key)(KeyModifierMask.MaskCtrl | KeyModifierMask.MaskShift) | key;

    void BuildMenus()
    {
        PopupMenu recipes = new();
        List<string> recipePaths = Recipes.Paths(ProjectFiles.With());
        foreach (string recipe in recipePaths)
        {
            recipes.AddItem(Recipes.Title(recipe));
        }
        recipes.IndexPressed += index => AddRecipe(recipePaths[(int)index]);
        AddMenu("File",
        [
            new("Save", "save", Ctrl(Key.S), Save),
            new("Revert to saved", "restart", Run: Revert),
            null,
            new("Open in text editor", "file", Run: () => OS.ShellOpen(ProjectSettings.GlobalizePath(FilePath))),
            null,
            new("Back to games", "back", Ctrl(Key.W), Leave),
        ]);
        AddMenu("Edit",
        [
            new("Undo", "undo", Ctrl(Key.Z), Undo),
            new("Redo", "redo", Ctrl(Key.Y), Redo),
            null,
            new("Rename selected", "rename", Run: Outline.StartRename),
            new("Duplicate selected", "duplicate", Ctrl(Key.D), () =>
            {
                if (Named.ContainsKey(Section) && Entry.Length > 0)
                {
                    Duplicate(Section, Entry);
                }
            }),
            new("Delete selected", "trash", Run: () =>
            {
                if (Named.ContainsKey(Section) && Entry.Length > 0)
                {
                    RemoveEntry(Section, Entry);
                }
            }),
            null,
            new("Command palette", "search", Ctrl(Key.P), OpenPalette),
        ]);
        AddMenu("Add", [.. AddItems(), null, new MenuRow("Recipe", Submenu: recipes)]);
        AddMenu("View",
        [
            new("Outline", "outline", CtrlShift(Key.E), () => ShowSidebarPage(0)),
            new("Recipes", "recipes", CtrlShift(Key.R), () => ShowSidebarPage(1)),
            new("Building blocks", "blocks", CtrlShift(Key.B), () => ShowSidebarPage(2)),
            null,
            new("Table", "table", Ctrl(Key.Key1), () => ShowTab(TableTab)),
            new("Deck", "deck", Ctrl(Key.Key2), () => ShowTab(DeckTab)),
            new("Game file", "file", Ctrl(Key.Key3), () => ShowTab(FileTab)),
            null,
            new("Show or hide sidebar", "panel_left", Ctrl(Key.B), () => GetNode<Control>("%Sidebar").Visible = !GetNode<Control>("%Sidebar").Visible),
            new("Show or hide inspector", "panel_right", (Key)(KeyModifierMask.MaskCtrl | KeyModifierMask.MaskAlt) | Key.B, () => Inspector.Visible = !Inspector.Visible),
            new("Show or hide problems", "panel_bottom", Ctrl(Key.J), ToggleProblems),
        ]);
        AddMenu("Game",
        [
            new("Play or stop", "play", Key.F5, () => SetPlaying(!playing)),
            new("Restart", "restart", Ctrl(Key.R), Restart),
        ]);
        AddMenu("Help",
        [
            new("Tutorial", "book", Run: () => OpenDoc("res://README.md")),
            new("Game file reference", "book", Key.F1, () => OpenDoc("res://docs/reference.md")),
        ]);
    }

    /// The things the Add menu (and the outline's + button) can add.
    List<MenuRow?> AddItems() =>
    [
        new("Zone", "zone", Run: () => AddEntry("Table")),
        new("Action", "actions", Run: () => AddEntry("Actions")),
        new("Trigger", "triggers", Run: () => AddEntry("Triggers")),
        new("Condition", "conditions", Run: () => AddEntry("Conditions")),
        new("Variable", "variables", Run: AddVariable),
        new("Deck", "deck", Run: MakeNewDeck),
    ];

    void AddVariable()
    {
        SelectSection("Variables");
        Inspector.AddTableEntry("variables");
    }

    void AddMenu(string title, List<MenuRow?> rows)
    {
        PopupMenu menu = Popup(rows, title);
        menu.Name = title;
        GetNode("%Menus").AddChild(menu);
    }

    /// A menu from its rows (null for a line); with a title, its commands also go in the command palette.
    PopupMenu Popup(List<MenuRow?> rows, string title = "")
    {
        PopupMenu menu = new();
        List<Action> actions = [];
        foreach (MenuRow? row in rows)
        {
            if (row == null)
            {
                menu.AddSeparator();
            }
            else if (row.Submenu != null)
            {
                row.Submenu.Name = row.Label;
                menu.AddChild(row.Submenu);
                menu.AddSubmenuNodeItem(row.Label, row.Submenu);
            }
            else
            {
                menu.AddIconItem(EditorIcons.Icon(row.Icon), row.Label, actions.Count, row.Shortcut);
                actions.Add(row.Run!);
                if (title.Length > 0)
                {
                    string shortcut = row.Shortcut != Key.None ? OS.GetKeycodeString(row.Shortcut) : "";
                    commands.Add(new(row.Label, title + (shortcut.Length > 0 ? "  " + shortcut : ""), row.Icon, row.Run!));
                }
            }
        }
        menu.IdPressed += id => actions[(int)id]();
        return menu;
    }

    void BuildBars()
    {
        foreach ((string node, string icon, string tooltip, Action pressed) in new (string, string, string, Action)[]
        {
            ("%BackButton", "back", "Back to games (Ctrl+W)", Leave), ("%UndoButton", "undo", "Undo (Ctrl+Z)", Undo),
            ("%RedoButton", "redo", "Redo (Ctrl+Y)", Redo), ("%SaveButton", "save", "Save (Ctrl+S)", Save),
            ("%RestartButton", "restart", "Restart the game (Ctrl+R)", Restart),
        })
        {
            Button button = GetNode<Button>(node);
            button.Icon = EditorIcons.Icon(icon);
            button.TooltipText = tooltip;
            button.Pressed += pressed;
        }
        GetNode<Button>("%PlayButton").Pressed += () => SetPlaying(!playing);
        GetNode<Button>("%ProblemStatus").Pressed += ToggleProblems;
        GetNode<Button>("%PaletteStatus").Pressed += OpenPalette;
        Button closeProblems = GetNode<Button>("%CloseProblems");
        closeProblems.Icon = EditorIcons.Icon("close", Colors.White, 0.85f);
        closeProblems.Pressed += ToggleProblems;
        Problems.ItemActivated += () => GoToProblem(SelectedProblem());
        Problems.ItemSelected += () =>
        {
            // A right-click selects too, but it's for the copy menu, not for going there.
            if (!Godot.Input.IsMouseButtonPressed(MouseButton.Right))
            {
                GoToProblem(SelectedProblem());
            }
        };
        PopupMenu copyMenu = Popup(
        [
            new("Copy", "duplicate", Ctrl(Key.C), () => DisplayServer.ClipboardSet(SelectedProblem())),
            new("Copy all problems", "duplicate", Run: () => DisplayServer.ClipboardSet(string.Join("\n", problems))),
        ]);
        AddChild(copyMenu);
        Problems.ItemMouseSelected += (at, button) =>
        {
            if (button == (long)MouseButton.Right && problems.Count > 0)
            {
                copyMenu.Popup(new Rect2I((Vector2I)(Problems.GetGlobalTransform() * at), Vector2I.Zero));
            }
        };
        Problems.GuiInput += @event =>
        {
            if (@event is InputEventKey { Pressed: true, Keycode: Key.C, CtrlPressed: true } && Problems.GetSelected() != null)
            {
                DisplayServer.ClipboardSet(SelectedProblem());
                Problems.AcceptEvent();
            }
        };
        ShowMode();
    }

    string SelectedProblem() => Problems.GetSelected()?.GetMetadata(0).AsString() ?? "";

    void BuildSidebar()
    {
        ButtonGroup group = new() { AllowUnpress = true };
        Container activities = GetNode<Container>("%Activities");
        for (int i = 0; i < SidebarPages.Length; i++)
        {
            int page = i;
            Button button = new()
            {
                ThemeTypeVariation = "ActivityButton", Icon = EditorIcons.Icon(SidebarPages[i].Icon, Colors.White, 1.4f), TooltipText = SidebarPages[i].Title,
                ToggleMode = true, ButtonGroup = group, FocusMode = FocusModeEnum.None,
            };
            button.Pressed += () =>
            {
                GetNode<Control>("%Sidebar").Visible = button.ButtonPressed;
                if (button.ButtonPressed)
                {
                    ShowSidebarPage(page);
                }
            };
            activities.AddChild(button);
        }
        activities.GetChild<Button>(0).ButtonPressed = true;
        PopupMenu addMenu = Popup(AddItems());
        AddChild(addMenu);
        Button addButton = GetNode<Button>("%AddButton");
        addButton.Pressed += () => addMenu.Popup(new Rect2I((Vector2I)(addButton.GlobalPosition + new Vector2(0, addButton.Size.Y)), Vector2I.Zero));
        addButton.Icon = EditorIcons.Icon("plus");
        addButton.TooltipText = "Add a zone, action, trigger...";
        LineEdit filter = GetNode<LineEdit>("%OutlineFilter");
        filter.RightIcon = EditorIcons.Icon("search", EditorTheme.TextDim);
        filter.TextChanged += text =>
        {
            Outline.Filter = text;
            FillOutline();
        };
        Outline.Picked += Pick;
        Outline.AddAsked += AddEntry;
        Outline.RenameAsked += RenameEntry;
        Outline.DuplicateAsked += Duplicate;
        Outline.RemoveAsked += RemoveEntry;
        GetNode<RecipeBrowser>("%RecipesPage").AddAsked += AddRecipe;
        GetNode<BlockBrowser>("%BlocksPage").InsertAsked += InsertBlock;
        ShowSidebarPage(0);
    }

    void BuildCenter()
    {
        foreach ((string title, string icon) in Tabs)
        {
            CenterTabs.AddTab(title, EditorIcons.Icon(icon, Colors.White, 0.85f));
        }
        CenterTabs.TabChanged += tab => OnTabChanged((int)tab);
        Button addZone = GetNode<Button>("%AddZoneButton");
        addZone.Icon = EditorIcons.Icon("plus", Colors.White, 0.85f);
        addZone.Pressed += () => AddEntry("Table");
        preview = GD.Load<PackedScene>(MainScene).Instantiate<Main>();
        preview.RulesPath = FilePath;
        preview.Embedded = true;
        preview.GivenFiles = FilesGiven();
        // The table looks as it does when played, not in the editor's own style.
        preview.Theme = ThemeDB.GetDefaultTheme();
        preview.SetAnchorsPreset(LayoutPreset.TopLeft);
        Center.AddChild(preview);
        Center.Resized += FitPreview;
        preview.ShowPlayControls(false);
        TableView table = preview.GetNode<TableView>("%TableView");
        dragger = new(table);
        dragger.ZonePicked += zone => Pick("Table", zone.Kind);
        dragger.ZoneMoved += MoveZone;
        table.AddChild(dragger);
        deckTable.SetAnchorsPreset(LayoutPreset.FullRect);
        deckTable.Edited += DeckEdited;
        deckTable.PreviewSpot.Theme = ThemeDB.GetDefaultTheme();
        GetNode("%DeckPage").AddChild(deckTable);
        FileView.Edited += FileEdited;
        ShowTab(TableTab);
    }

    void ShowSidebarPage(int index)
    {
        GetNode<Control>("%Sidebar").Visible = true;
        GetNode<Container>("%Activities").GetChild<Button>(index).ButtonPressed = true;
        GetNode<Label>("%SidebarTitle").Text = SidebarPages[index].Title.ToUpperInvariant();
        for (int i = 0; i < SidebarPages.Length; i++)
        {
            GetNode<Control>("%" + SidebarPages[i].Node).Visible = i == index;
        }
        GetNode<Control>("%AddButton").Visible = index == 0;
    }

    internal void ShowTab(int index)
    {
        CenterTabs.CurrentTab = index;
        OnTabChanged(index);
    }

    void OnTabChanged(int index)
    {
        Center.Visible = index == TableTab;
        GetNode<Control>("%DeckPage").Visible = index == DeckTab;
        FileView.Visible = index == FileTab;
        GetNode<Control>("%TableTools").Visible = index == TableTab && !playing;
        if (index == DeckTab)
        {
            ShowDeck();
        }
        else if (index == FileTab)
        {
            FileView.ShowText(Document.Text);
            GoToInFile();
        }
    }

    void ToggleProblems()
    {
        Control bottom = GetNode<Control>("%BottomPanel");
        bottom.Visible = !bottom.Visible;
    }

    static void OpenDoc(string path)
    {
        OS.ShellOpen(ProjectSettings.GlobalizePath(path));
    }

    /// The game is laid out at the play screen's size and shrunk to fit, so it looks as it will when played (only
    /// smaller) instead of crowding full-size cards together.
    void FitPreview()
    {
        Vector2 playSize = new(ProjectSettings.GetSetting("display/window/size/viewport_width").AsSingle(), ProjectSettings.GetSetting("display/window/size/viewport_height").AsSingle());
        float shrink = Mathf.Min(1, Mathf.Min(Center.Size.X / playSize.X, Center.Size.Y / playSize.Y));
        preview.Size = playSize;
        preview.Scale = new Vector2(shrink, shrink);
        preview.Position = (Center.Size - playSize * shrink) / 2;
    }

    // Changing the game

    /// Every edit from the forms comes through here, so it can be undone. A null value takes the key out.
    public void Change(List<object> path, object? value)
    {
        Remember(path);
        if (value == null)
        {
            Document.Remove(path);
        }
        else
        {
            Document.SetValue(path, value);
        }
        Changed(false);
    }

    /// Bigger edits (adding, renaming, removing), as something that changes the document.
    public void Edit(Action how)
    {
        // Menu buttons don't take focus, so a box being typed in hasn't sent its text yet; it would be lost on rebuild.
        GetViewport().GuiReleaseFocus();
        Remember([]);
        how();
        Changed(true);
    }

    /// Adds a ready-made piece of game (Recipes), then says what it does and what to adjust.
    internal void AddRecipe(string recipe)
    {
        Edit(() => Recipes.AddTo(Document, ProjectFiles.With(), recipe));
        recipeAdded.Title = "Added: " + Recipes.Title(recipe);
        recipeAdded.DialogText = Recipes.Description(ProjectFiles.With(), recipe);
        recipeAdded.PopupCentered();
    }

    internal void Undo()
    {
        if (history.Count > 0)
        {
            future.Add(State());
            RestoreState(TakeLast(history));
        }
    }

    void Redo()
    {
        if (future.Count > 0)
        {
            history.Add(State());
            RestoreState(TakeLast(future));
        }
    }

    static EditorState TakeLast(List<EditorState> states)
    {
        EditorState last = states[^1];
        states.RemoveAt(states.Count - 1);
        return last;
    }

    void Save()
    {
        GetViewport().GuiReleaseFocus();
        GameFiles files = ProjectFiles.With();
        if (!Document.Save(files))
        {
            ShowMessage("Couldn't save " + FilePath);
            return;
        }
        foreach ((string path, string text) in deckTexts)
        {
            System.IO.File.WriteAllText(files.FullPath(path), text);
        }
        savedState = State();
        UpdateTitle();
        ShowMessage("Saved " + FilePath.GetFile());
    }

    void Revert()
    {
        if (!State().SameAs(savedState))
        {
            Remember([]);
            RestoreState(savedState);
            ShowMessage("Back to the saved file (Ctrl+Z brings your changes back)");
        }
    }

    void Remember(List<object> path)
    {
        ulong now = Time.GetTicksMsec();
        if (path.Count == 0 || !path.SequenceEqual(lastChangePath) || now - lastChangeTime > MergeTime)
        {
            history.Add(State());
            future.Clear();
        }
        lastChangePath = path;
        lastChangeTime = now;
    }

    EditorState State() => new(Document.Text, new(deckTexts));

    void RestoreState(EditorState state)
    {
        Document.SetText(state.Rules);
        deckTexts = new(state.Decks);
        lastChangePath = [];
        if (Entry.Length > 0 && !EntriesOf(Section).Any(row => row.Key == Entry))
        {
            Entry = "";
        }
        Changed(true);
    }

    /// After any change: the outline, title and game file catch up, and the table and problems are checked again shortly.
    void Changed(bool rebuildDetails)
    {
        UpdateTitle();
        FillOutline();
        if (rebuildDetails)
        {
            Inspector.ShowPage();
        }
        if (FileView.Visible)
        {
            FileView.ShowText(Document.Text);
        }
        if (GetNode<Control>("%DeckPage").Visible && rebuildDetails)
        {
            ShowDeck();
        }
        reloadTimer.Start(ReloadDelay);
    }

    internal void FileEdited(string text)
    {
        Remember(["game file"]);
        Document.SetText(text);
        Changed(true);
    }

    void UpdateTitle()
    {
        bool unsaved = !State().SameAs(savedState);
        string title = Document.GetValue(["title"]) as string ?? "";
        GetNode<Label>("%FileLabel").Text = (title.Length > 0 ? title + "  ·  " : "") + FilePath.GetFile() + (unsaved ? "  ●" : "");
        Label saveStatus = GetNode<Label>("%SaveStatus");
        saveStatus.Text = unsaved ? "Unsaved changes" : "Saved";
        saveStatus.AddThemeColorOverride("font_color", unsaved ? EditorTheme.Text : EditorTheme.TextDim);
        List<string> counts = [];
        foreach ((string key, string noun) in new[] { ("zones", "zone"), ("actions", "action"), ("triggers", "trigger") })
        {
            int count = (Document.GetValue([key]) as Dictionary<string, object?>)?.Count ?? 0;
            counts.Add($"{count} {noun}{(count == 1 ? "" : "s")}");
        }
        object? players = Document.GetValue(["players"], 1L);
        string playersText = GameDocument.OneLine(players);
        counts.Add($"{playersText} player{(playersText == "1" ? "" : "s")}");
        GetNode<Label>("%SummaryStatus").Text = string.Join("  ·  ", counts);
    }

    /// The game file's and decks' text as edited here, played and checked in place of the files on disk.
    Dictionary<string, string> FilesGiven()
    {
        Dictionary<string, string> given = new(deckTexts) { [FilePath] = Document.Text };
        return given;
    }

    void Reload()
    {
        preview.GivenFiles = FilesGiven();
        preview.LoadGame();
        dragger.QueueRedraw();
        if (Document.ParseError.Length > 0)
        {
            ShowProblems(["The game file can't be read: " + Document.ParseError]);
        }
        else
        {
            CheckedGame = new(Files, FilePath, 1);
            ShowProblems(CheckedGame.Errors);
        }
        Match line = Regex.Match(Document.ParseError, @"^line (\d+)");
        FileView.ShowErrorLine(line.Success ? int.Parse(line.Groups[1].Value) - 1 : -1);
    }

    void ShowProblems(List<string> found)
    {
        problems = [.. found];
        Problems.Clear();
        TreeItem root = Problems.CreateItem();
        foreach (string problem in found)
        {
            TreeItem item = Problems.CreateItem(root);
            item.SetText(0, problem);
            item.SetIcon(0, EditorIcons.Icon("error", EditorTheme.Error));
            item.SetMetadata(0, problem);
            item.SetTooltipText(0, problem);
        }
        if (found.Count == 0)
        {
            TreeItem none = Problems.CreateItem(root);
            none.SetText(0, "No problems found.");
            none.SetIcon(0, EditorIcons.Icon("ok", EditorTheme.Good));
            none.SetSelectable(0, false);
            none.SetMetadata(0, "");
        }
        GetNode<Label>("%ProblemCount").Text = found.Count > 0 ? found.Count.ToString() : "";
        Button status = GetNode<Button>("%ProblemStatus");
        status.Text = found.Count > 0 ? $"{found.Count} problem{(found.Count == 1 ? "" : "s")}" : "No problems";
        status.Icon = EditorIcons.Icon(found.Count > 0 ? "error" : "ok", found.Count > 0 ? EditorTheme.Error : EditorTheme.Text, 0.8f);
        FillOutline();
    }

    /// A short note in the status bar that goes away by itself.
    public void ShowMessage(string text)
    {
        GetNode<Label>("%StatusMessage").Text = text;
        messageTimer.Start(MessageTime);
    }

    internal void SetPlaying(bool on)
    {
        playing = on;
        dragger.Visible = !on;
        preview.ShowPlayControls(on);
        if (on)
        {
            ShowTab(TableTab);
        }
        GetNode<Control>("%TableTools").Visible = CenterTabs.CurrentTab == TableTab && !on;
        ShowMode();
        Reload();
    }

    void Restart()
    {
        if (!playing)
        {
            SetPlaying(true);
        }
        else
        {
            Reload();
        }
    }

    void ShowMode()
    {
        Button play = GetNode<Button>("%PlayButton");
        play.Text = playing ? "Stop" : "Play";
        play.Icon = EditorIcons.Icon(playing ? "stop" : "play");
        play.TooltipText = (playing ? "Stop playing" : "Play the game as it is now, saved or not") + " (F5)";
        GetNode<Control>("%RestartButton").Visible = playing;
        Button mode = GetNode<Button>("%ModeStatus");
        mode.Text = playing ? "Playing" : "Editing";
        mode.Icon = EditorIcons.Icon(playing ? "play" : "rename", Colors.White, 0.75f);
        GetNode<Control>("%StatusBar").AddThemeStyleboxOverride("panel",
            EditorTheme.Box(playing ? EditorTheme.Playing : EditorTheme.Panel, EditorTheme.Border, [0, 1, 0, 0], 0, new Vector2(6, 0)));
    }

    void Leave()
    {
        if (State().SameAs(savedState))
        {
            GetTree().ChangeSceneToFile(TitleScene);
            return;
        }
        ConfirmationDialog dialog = new() { DialogText = "Leave without saving your changes?", OkButtonText = "Leave" };
        dialog.Confirmed += () => GetTree().ChangeSceneToFile(TitleScene);
        AddChild(dialog);
        dialog.PopupCentered();
    }

    // Sections and entries

    /// Shows a section, with its first entry if it has any.
    internal void SelectSection(string name)
    {
        Section = name;
        List<OutlineEntry> entries = EntriesOf(name);
        Entry = entries.Count > 0 ? entries[0].Key : "";
        SelectionChanged();
    }

    public void SelectEntry(string key)
    {
        Entry = key;
        SelectionChanged();
    }

    /// Picked in the outline or on the table: a section row shows the section on its own (the Deck's first deck).
    void Pick(string pickedSection, string pickedEntry)
    {
        if (pickedSection == "Deck" && pickedEntry.Length == 0)
        {
            SelectSection("Deck");
            return;
        }
        Section = pickedSection;
        Entry = pickedEntry;
        SelectionChanged();
    }

    void SelectionChanged()
    {
        Outline.ShowSelected(Section, Entry);
        dragger.SelectedKind = Section == "Table" ? Entry : "";
        dragger.QueueRedraw();
        if (Section == "Deck" && Entry.Length > 0)
        {
            deckShown = Entry;
            if (CenterTabs.CurrentTab != FileTab)
            {
                ShowTab(DeckTab);
            }
        }
        else if (Section == "Table" && CenterTabs.CurrentTab == DeckTab)
        {
            ShowTab(TableTab);
        }
        else if (CenterTabs.CurrentTab == FileTab)
        {
            GoToInFile();
        }
        Inspector.ShowPage();
    }

    List<OutlineEntry> EntriesOf(string ofSection)
    {
        if (Named.TryGetValue(ofSection, out NamedSection? kind))
        {
            return Document.GetValue([kind.Key]) is Dictionary<string, object?> values
                ? values.Select(pair => new OutlineEntry(pair.Key, pair.Key, Detail(ofSection, pair.Value))).ToList()
                : [];
        }
        if (ofSection == "Deck")
        {
            return AllDecks().Select(deck => new OutlineEntry(deck, deck.GetFile())).ToList();
        }
        return [];
    }

    /// A word or two shown beside an entry in the outline: a zone's layout and copies, how an action is done.
    static string Detail(string ofSection, object? value)
    {
        if (value is not Dictionary<string, object?> settings)
        {
            return ofSection == "Actions" ? "button" : "";
        }
        if (ofSection == "Table")
        {
            string copies = settings.GetValueOrDefault("per_player") is true ? " ×P" : settings.GetValueOrDefault("copies") is double count ? $" ×{count}" : "";
            return Values.Text(settings.GetValueOrDefault("layout", "row")) + copies;
        }
        if (ofSection == "Actions")
        {
            foreach (string way in new[] { "drag", "click", "double_click" })
            {
                if (settings.ContainsKey(way))
                {
                    return way.Replace("_", " ");
                }
            }
            return settings.GetValueOrDefault("button") is false ? "ask" : "button";
        }
        return "";
    }

    void FillOutline()
    {
        List<OutlineSection> sections = Sections.Select(name =>
        {
            List<OutlineEntry> entries = EntriesOf(name);
            return Named.TryGetValue(name, out NamedSection? kind) ? new OutlineSection(name, entries, kind.Noun, entries.Count) : new OutlineSection(name, entries);
        }).ToList();
        Dictionary<string, string> marks = [];
        foreach (string problem in problems)
        {
            if (PlaceOf(problem) is { } place)
            {
                marks[place.Section + (place.Entry.Length > 0 ? "/" + place.Entry : "")] = problem;
                marks[place.Section] = problem;
            }
        }
        Outline.ShowSections(sections, marks, Section, Entry);
    }

    List<string> DecksUsed()
    {
        List<string> decks = [];
        foreach (Dictionary<string, object?> zone in (Document.GetValue(["zones"]) as Dictionary<string, object?>)?.Values.OfType<Dictionary<string, object?>>() ?? [])
        {
            if (zone.GetValueOrDefault("cards") is string deck && !decks.Contains(deck))
            {
                decks.Add(deck);
            }
        }
        return decks;
    }

    /// The decks the zones use, and any new ones made here.
    List<string> AllDecks()
    {
        List<string> decks = DecksUsed();
        decks.AddRange(deckTexts.Keys.Where(deck => !decks.Contains(deck)));
        return decks;
    }

    /// Adds an entry with a name not used yet, then lets you type its real name in the outline.
    public void AddEntry(string toSection)
    {
        NamedSection kind = Named[toSection];
        string name = kind.First;
        for (int number = 2; Document.Has([kind.Key, name]); number++)
        {
            name = kind.First.IsValidIdentifier() ? $"{kind.First}_{number}" : $"{kind.First} {number}";
        }
        Section = toSection;
        Edit(() => Document.SetValue([kind.Key, name], kind.NewValue()));
        SelectEntry(name);
        ShowSidebarPage(0);
        Outline.StartRename();
    }

    internal void RenameEntry(string ofSection, string old, string newName)
    {
        NamedSection kind = Named[ofSection];
        if (!GoodName(ofSection, newName))
        {
            ShowMessage($"{kind.Noun.Capitalize()} names must be one word: letters, numbers and _.");
            FillOutline();
            return;
        }
        if (Document.Has([kind.Key, newName]))
        {
            ShowMessage($"There's already a {kind.Noun} called {newName}.");
            FillOutline();
            return;
        }
        Section = ofSection;
        Edit(() => Document.RenameKey([kind.Key, old], newName));
        SelectEntry(newName);
        if (Regex.IsMatch(Document.Text, $@"\b{Regex.Escape(old)}\b"))
        {
            ShowMessage($"Renamed. Steps that still say {old} need changing too.");
        }
    }

    internal void Duplicate(string ofSection, string key)
    {
        NamedSection kind = Named[ofSection];
        string suffix = key.IsValidIdentifier() ? "_copy" : " copy";
        string copy = key + suffix;
        while (Document.Has([kind.Key, copy]))
        {
            copy += suffix;
        }
        Section = ofSection;
        Edit(() => Document.SetValue([kind.Key, copy], Document.GetValue([kind.Key, key])));
        SelectEntry(copy);
    }

    internal void RemoveEntry(string ofSection, string key)
    {
        Section = ofSection;
        Entry = "";
        Edit(() => Document.Remove([Named[ofSection].Key, key]));
        SelectionChanged();
        ShowMessage($"Deleted {Named[ofSection].Noun} {key} (Ctrl+Z to undo)");
    }

    /// Zone, trigger and condition names are used in steps, so they must be single words; actions can say anything.
    static bool GoodName(string ofSection, string name) => ofSection switch
    {
        "Actions" => name.Length > 0,
        "Conditions" => Regex.IsMatch(name, @"^\s*[A-Za-z_][A-Za-z0-9_]*\s*(?:\([^)]*\))?\s*$"),
        _ => name.IsValidIdentifier(),
    };

    /// A new deck file (written on Save), for a zone's cards.
    void MakeNewDeck()
    {
        string baseName = DecksFolder + FilePath.GetFile().GetBaseName() + "_deck";
        string path = baseName + ".csv";
        for (int number = 2; FileAccess.FileExists(path) || deckTexts.ContainsKey(path); number++)
        {
            path = $"{baseName}_{number}.csv";
        }
        Remember([]);
        deckTexts[path] = NewDeck;
        Changed(false);
        Section = "Deck";
        SelectEntry(path);
        ShowMessage($"New deck {path.GetFile()}: give it to a zone as its cards, under Table.");
    }

    string DeckText(string deck) => deckTexts.TryGetValue(deck, out string? text) ? text : ProjectFiles.With().Exists(deck) ? ProjectFiles.With().Read(deck) : "";

    void ShowDeck()
    {
        List<string> decks = AllDecks();
        if (!decks.Contains(deckShown))
        {
            deckShown = decks.Count > 0 ? decks[0] : "";
        }
        deckTable.Visible = deckShown.Length > 0;
        GetNode<Control>("%NoDeck").Visible = deckShown.Length == 0;
        if (deckShown.Length > 0)
        {
            Dictionary<string, object?> face = Document.GetValue(["card_face"]) as Dictionary<string, object?>
                ?? new() { ["corner"] = "name", ["title"] = "name" };
            deckTable.ShowDeck(DeckText(deckShown), face);
        }
    }

    // Steps, the deck and the table

    /// A box for a step or condition, knowing the game's names as last checked.
    public SmartField SmartField(string text)
    {
        if (CheckedGame == null)
        {
            return new(text, new([], DeckColumns(), CheckStep, [], []));
        }
        GameRules rules = CheckedGame.Rules;
        List<string> names = [.. CheckedGame.InputNames(), .. rules.Conditions.Keys];
        List<string> plainConditions = rules.Conditions.Keys.Where(name => rules.ConditionParameters[name].Count == 0).ToList();
        List<string> forPlayers = [.. rules.PlayerVariables.Keys, .. plainConditions];
        List<string> actions = [.. rules.Actions.Keys];
        List<string> triggers = [.. rules.Triggers.Keys];
        Dictionary<string, List<string>> quoted = new()
        {
            ["offer"] = actions, ["offer_until"] = actions, ["ask"] = actions, ["ask_all"] = [.. actions, .. triggers],
            ["ask_others"] = [.. actions, .. triggers], ["trigger"] = triggers, ["matching"] = plainConditions,
            ["most"] = forPlayers, ["least"] = forPlayers, ["players_with_most"] = forPlayers, ["players_with_least"] = forPlayers,
            ["players_where"] = forPlayers, ["reset"] = [.. rules.Variables.Keys, .. rules.PlayerVariables.Keys, .. rules.TeamVariables.Keys],
        };
        Dictionary<string, string> signatures = rules.ConditionParameters.Where(pair => pair.Value.Count > 0)
            .ToDictionary(pair => pair.Key, pair => $"{pair.Key}({string.Join(", ", pair.Value)})");
        return new(text, new(names, DeckColumns(), CheckStep, quoted, signatures));
    }

    internal List<string> CheckStep(string text) => CheckedGame != null ? new StepCompiler(CheckedGame).ProblemsWith(text) : [];

    public List<string> DeckColumns()
    {
        List<string> columns = [];
        foreach (string deck in AllDecks())
        {
            List<List<string>> rows = DeckLoader.Rows(DeckText(deck));
            columns.AddRange((rows.Count > 0 ? rows[0] : []).Where(column => !columns.Contains(column)));
        }
        return columns;
    }

    /// A building block picked in the sidebar goes where a step (or the game file) was last being typed.
    void InsertBlock(string text)
    {
        if (lastCode != null && IsInstanceValid(lastCode) && lastCode.IsVisibleInTree())
        {
            lastCode.GrabFocus();
            lastCode.InsertTextAtCaret(text);
        }
        else
        {
            DisplayServer.ClipboardSet(text);
            ShowMessage($"Copied {text}: click into a step and paste it.");
        }
    }

    internal void OpenPalette()
    {
        List<PaletteEntry> entries = [.. commands];
        foreach (string name in Sections)
        {
            entries.Add(new(name, "Go to", GameOutline.Icons[name], () => Pick(name, "")));
            foreach (OutlineEntry row in EntriesOf(name))
            {
                entries.Add(new(row.Label, name, GameOutline.Icons[name], () => Pick(name, row.Key)));
            }
        }
        foreach (string recipe in Recipes.Paths(ProjectFiles.With()))
        {
            entries.Add(new("Add recipe: " + Recipes.Title(recipe), "Recipes", "recipes", () => AddRecipe(recipe)));
        }
        palette.Open(entries);
    }

    internal void DeckEdited(string text)
    {
        Remember(["deck", deckShown]);
        deckTexts[deckShown] = text;
        Changed(false);
    }

    /// Dragging a zone on the table: per-player zones placed round the table move seat 1's spot, grids move as a
    /// whole, and zones placed one by one ("anchors") move just that one.
    internal void MoveZone(Zone zone, Vector2 spot)
    {
        List<object> path = ["zones", zone.Kind];
        Dictionary<string, object?> definition = Document.GetValue(path) as Dictionary<string, object?> ?? [];
        int number = zone.Name != zone.Kind ? int.Parse(zone.Name[(zone.Kind.Length + 1)..]) : 0;
        int players = preview.Game?.Rules.Players ?? 1;
        if (number > 0 && definition.ContainsKey("anchors"))
        {
            path.AddRange(["anchors", number - 1]);
        }
        else
        {
            path.Add("anchor");
            Vector2 center = new(0.5f, 0.5f);
            if (number > 1 && definition.GetValueOrDefault("per_player") is true)
            {
                spot = center + (spot - center).Rotated(-Mathf.Tau * (number - 1) / players);
            }
            else if (number > 0 && definition.ContainsKey("grid"))
            {
                List<object?> start = definition.GetValueOrDefault("anchor") as List<object?> ?? [0.5, 0.5];
                spot = new Vector2((float)Values.ToDouble(start[0]), (float)Values.ToDouble(start[1])) + spot - new Vector2(zone.Anchor.X, zone.Anchor.Y);
            }
        }
        Change(path, new List<object?> { Mathf.Snapped((double)spot.X, 0.005), Mathf.Snapped((double)spot.Y, 0.005) });
        Inspector.ShowPage();
    }

    // Problems and the game file

    void GoToProblem(string problem)
    {
        if (problem.Length == 0)
        {
            return;
        }
        if (Document.ParseError.Length > 0)
        {
            ShowTab(FileTab);
            FileView.SetCaretLine(Math.Max(FileView.ErrorLine, 0));
            FileView.CenterViewportToCaret();
            FileView.GrabFocus();
            return;
        }
        if (PlaceOf(problem) is { } place)
        {
            ShowSidebarPage(0);
            Pick(place.Section, place.Entry);
        }
    }

    /// Problems name what they're about: a zone, or a step's text, which is looked up in the file. Gives the section
    /// and entry, or null if it can't tell.
    (string Section, string Entry)? PlaceOf(string problem)
    {
        Match zone = Regex.Match(problem, "^Zone \"(\\w+)\"");
        if (zone.Success)
        {
            return ("Table", zone.Groups[1].Value);
        }
        Match quoted = Regex.Match(problem, "\"([^\"]+)\"");
        List<object>? path = quoted.Success ? PathHolding(Document.Data, quoted.Groups[1].Value, []) : null;
        if (path == null || path.Count == 0 || path[0] is not string key || !SectionOf.TryGetValue(key, out string? foundSection))
        {
            return null;
        }
        return (foundSection, path.Count >= 2 && Named.ContainsKey(foundSection) ? path[1].ToString()! : "");
    }

    /// The path of the first value or key in the file equal to the text.
    static List<object>? PathHolding(object? value, string text, List<object> path)
    {
        if (value is string found && found == text)
        {
            return path;
        }
        if (value is Dictionary<string, object?> members)
        {
            foreach ((string key, object? member) in members)
            {
                if (key == text)
                {
                    return [.. path, key];
                }
                if (PathHolding(member, text, [.. path, key]) is { } inside)
                {
                    return inside;
                }
            }
        }
        if (value is List<object?> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (PathHolding(items[i], text, [.. path, i]) is { } inside)
                {
                    return inside;
                }
            }
        }
        return null;
    }

    /// In the Game file tab, shows the selected entry's place in the text.
    void GoToInFile()
    {
        List<object> path = [];
        if (Named.TryGetValue(Section, out NamedSection? kind))
        {
            path = Entry.Length > 0 ? [kind.Key, Entry] : [kind.Key];
        }
        else
        {
            string? key = SectionOf.FirstOrDefault(pair => pair.Value == Section && Document.Has([pair.Key])).Key;
            if (key != null)
            {
                path = [key];
            }
        }
        if (path.Count > 0 && Document.Has(path))
        {
            FileView.GoTo(Document.OffsetOf(path));
        }
    }
}

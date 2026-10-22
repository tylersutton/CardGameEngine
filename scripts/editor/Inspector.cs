using Core;
using Editing;
using Godot;

/// The editor's right-hand panel: the selected part of the game (a zone, an action, the turn...) with its settings,
/// and at the bottom a description of whatever setting is under the mouse or being typed in. Edits go through the
/// editor (Change and Edit), so they can be undone.
public partial class Inspector : PanelContainer
{
    static readonly Dictionary<string, string> TriggerHelp = new()
    {
        ["start"] = "Runs when the game begins (and on Reload): deal the cards, set things up.",
        ["turn_start"] = "Runs after every end_turn().",
    };
    static readonly Dictionary<string, string> SectionHelp = new()
    {
        ["Game"] = "The game's name and a line about it, both shown on the title screen.",
        ["Table"] = "Zones are the places cards can be: decks, hands, piles. Drag a zone on the table to move it, or pick one to change it.",
        ["Deck"] = "The cards, one row per kind of card (a count column says how many copies). The card face says which columns are drawn on a card.",
        ["Players"] = "How many players there are, what they're called, and which seats the computer plays.",
        ["Variables"] = "Values the game keeps track of, with their starting values. Use their names in any step.",
        ["Actions"] = "What players can do. An action is done by a drag or click if it says so, otherwise by its button. For a drag or click, the first action in the file that allows it is done.",
        ["Turn"] = "Steps run again and again for whoever's turn it is (end_turn() passes it on). offer(['Play a card', 'Draw']) waits for the player to do one of those actions; actions never offered can be done any time. Leave it empty for games without turns, like solitaire.",
        ["Triggers"] = "Steps that run at the start, when a deck runs out, or when a step or card asks for them with trigger('name').",
        ["Conditions"] = "Named expressions: write one once and use its name in any step.",
        ["Scoring"] = "Live readouts in the side panel, recalculated after every change. Name players with name_of(1), and use viewer() for anything personal (\"your points\").",
        ["Melds"] = "Which groups of cards count as melds; this turns on melds(), deadwood() and layoff().",
    };
    static readonly (string Key, string Title, string Help)[] VariableTables =
    [
        ("variables", "Variables", "Shared values, with their starting values, e.g. phase \"draw\" or passes 0."),
        ("player_variables", "Player variables", "One of each per player (score_1, score_2); in steps, score means the current player's."),
        ("team_variables", "Team variables", "One of each per team (needs teams, under Players)."),
        ("constants", "Constants", "Values that never change, e.g. a points table shell_points [0, 0, 2, 4, 6]: read them in steps."),
    ];
    const int DescriptionHeight = 78;

    EditorScreen editor = null!;
    readonly Label kindLabel = new() { ThemeTypeVariation = "HeaderLabel" };
    readonly Label nameLabel = new() { ThemeTypeVariation = "TitleLabel", ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    readonly TextureRect headerIcon = new() { StretchMode = TextureRect.StretchModeEnum.KeepCentered, Modulate = EditorTheme.TextDim };
    readonly VBoxContainer content = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    readonly ScrollContainer scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
    readonly Label descriptionTitle = new() { ThemeTypeVariation = "TitleLabel" };
    readonly Label descriptionText = new() { ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    /// What the description says when no setting is being pointed at.
    string pageTitle = "";
    string pageHelp = "";
    /// A variable just added: its name box takes the focus once the page is built.
    string nameToFocus = "";

    GameDocument Document => editor.Document;

    public override void _Ready()
    {
        VBoxContainer column = new();
        column.AddThemeConstantOverride("separation", 0);
        AddChild(column);
        PanelContainer header = new() { ThemeTypeVariation = "Bar" };
        HBoxContainer headerRow = new();
        headerRow.AddThemeConstantOverride("separation", 6);
        headerRow.AddChild(headerIcon);
        headerRow.AddChild(kindLabel);
        headerRow.AddChild(nameLabel);
        header.AddChild(EditorTheme.Padded(headerRow, 10, 7));
        column.AddChild(header);
        content.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(EditorTheme.Padded(content, 10, 8));
        column.AddChild(scroll);
        PanelContainer description = new() { CustomMinimumSize = new Vector2(0, DescriptionHeight) };
        description.AddThemeStyleboxOverride("panel", EditorTheme.Box(EditorTheme.Panel, EditorTheme.Border, [0, 1, 0, 0], 0, new Vector2(10, 7)));
        VBoxContainer descriptionColumn = new();
        descriptionColumn.AddThemeConstantOverride("separation", 2);
        descriptionColumn.AddChild(descriptionTitle);
        descriptionColumn.AddChild(descriptionText);
        description.AddChild(descriptionColumn);
        column.AddChild(description);
    }

    public void Setup(EditorScreen gameEditor)
    {
        editor = gameEditor;
    }

    /// Shows a setting's help at the bottom, or the page's own when given nothing.
    public void Describe(string title = "", string help = "")
    {
        descriptionTitle.Text = title.Length > 0 ? title : pageTitle;
        descriptionText.Text = help.Length > 0 ? help : pageHelp;
    }

    // Pages

    /// Shows the editor's selected section and entry.
    public void ShowPage()
    {
        string section = editor.Section;
        string entry = editor.Entry;
        int keepScroll = scroll.ScrollVertical;
        foreach (Node child in content.GetChildren())
        {
            child.QueueFree();
        }
        headerIcon.Texture = EditorIcons.Icon(GameOutline.Icons[section]);
        pageTitle = section;
        pageHelp = SectionHelp[section];
        bool named = EditorScreen.Named.TryGetValue(section, out NamedSection? kind);
        if (named && entry.Length > 0)
        {
            kindLabel.Text = kind!.Noun.ToUpperInvariant();
            nameLabel.Text = entry;
            pageTitle = entry;
        }
        else if (section == "Deck" && entry.Length > 0)
        {
            kindLabel.Text = "DECK";
            nameLabel.Text = entry.GetFile();
        }
        else
        {
            kindLabel.Text = "";
            nameLabel.Text = section;
        }
        if (named && entry.Length == 0)
        {
            ShowOverview(section);
        }
        else
        {
            switch (section)
            {
                case "Game":
                    AddForm([], EditorFields.Game);
                    break;
                case "Table":
                    AddNote(["zones", entry]);
                    AddForm(["zones", entry], EditorFields.Zone);
                    break;
                case "Deck":
                    ShowCardFace(entry);
                    break;
                case "Actions":
                    ShowAction(entry);
                    break;
                case "Turn":
                    ShowTurn();
                    break;
                case "Triggers":
                    pageHelp = TriggerHelp.GetValueOrDefault(entry, $"Runs when a step calls trigger('{entry}'), or when a card names it in an on_<zone> column (then card is that card).");
                    AddNote(["triggers", entry]);
                    content.AddChild(new StepList(editor, ["triggers", entry]));
                    break;
                case "Conditions":
                    pageHelp = $"A named expression: write its name in any step or condition, or use matching(cards, '{entry}') to keep the cards it's true for (card is each card). A name like points(cards) takes parameters: use it as points(hand).";
                    AddNote(["conditions", entry]);
                    SmartField field = editor.SmartField(Values.Text(Document.GetValue(["conditions", entry], "")));
                    field.Committed += text => editor.Change(["conditions", entry], text);
                    content.AddChild(field);
                    break;
                case "Variables":
                    foreach ((string key, string title, string help) in VariableTables)
                    {
                        AddNameTable(key, title, help);
                    }
                    break;
                case "Players":
                    AddForm([], EditorFields.Players);
                    AddHeading("Bots", "Which seats the computer plays, and how well.");
                    AddForm(["bot"], EditorFields.Bot);
                    break;
                case "Scoring":
                    AddNameTable("status", "Status", SectionHelp["Scoring"], true);
                    AddForm([], EditorFields.Scoring);
                    break;
                case "Melds":
                    ShowMelds();
                    break;
            }
        }
        Describe();
        scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, keepScroll);
    }

    /// A section of named entries with none picked: what's in it, and a button to add one.
    void ShowOverview(string section)
    {
        NamedSection kind = EditorScreen.Named[section];
        List<string> entries = (Document.GetValue([kind.Key]) as Dictionary<string, object?>)?.Keys.ToList() ?? [];
        if (entries.Count == 0)
        {
            content.AddChild(new Label { Text = $"No {kind.Noun}s yet.", ThemeTypeVariation = "DimLabel" });
        }
        foreach (string name in entries)
        {
            Button row = new() { ThemeTypeVariation = "FlatButton", Text = name, Alignment = HorizontalAlignment.Left };
            row.Pressed += () => editor.SelectEntry(name);
            content.AddChild(row);
        }
        Button add = new()
        {
            ThemeTypeVariation = "AccentButton", Text = "Add a " + kind.Noun, Icon = EditorIcons.Icon("plus", Colors.White, 0.85f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
        };
        add.Pressed += () => editor.AddEntry(section);
        content.AddChild(add);
        content.AddChild(new Label { Text = SectionHelp[section], ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    void ShowCardFace(string deck)
    {
        if (deck.Length == 0)
        {
            content.AddChild(new Label
            {
                Text = "No deck yet: pick a zone under Table and choose its cards.", ThemeTypeVariation = "DimLabel",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
            return;
        }
        AddHeading("Card face", "Which deck columns are drawn on each card, and its colors.");
        AddForm(["card_face"], EditorFields.CardFace, editor.DeckColumns());
    }

    /// An action written as just a list of steps (a button) gets the full form once it needs more than steps.
    void ShowAction(string entry)
    {
        List<object> path = ["actions", entry];
        AddNote(path);
        if (Document.GetValue(path) is Dictionary<string, object?>)
        {
            AddForm(path, EditorFields.Action);
            return;
        }
        AddHeading("Steps", "What the button does.");
        content.AddChild(new StepList(editor, path));
        Button more = new()
        {
            ThemeTypeVariation = "FlatButton", Text = "Drag, click, choosing and more...", Icon = EditorIcons.Icon("plus", Colors.White, 0.8f),
            TooltipText = "Turn this button into a full action, with drag, click, when, choose and more", SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
        };
        more.Pressed += () => editor.Edit(() =>
        {
            object? steps = Document.GetValue(path);
            Document.SetValue(path, new Dictionary<string, object?> { ["steps"] = steps as List<object?> ?? [steps] });
        });
        content.AddChild(more);
    }

    /// The turn in words (TurnSummary), in a box above its steps.
    void ShowTurn()
    {
        AddNote(["turn"]);
        if (editor.CheckedGame != null)
        {
            PanelContainer box = new() { ThemeTypeVariation = "Card" };
            box.AddChild(new Label { Text = string.Join("\n", TurnSummary.Lines(editor.CheckedGame.Rules)), AutowrapMode = TextServer.AutowrapMode.WordSmart });
            content.AddChild(box);
        }
        AddHeading("Steps", SectionHelp["Turn"]);
        content.AddChild(new StepList(editor, ["turn"]));
    }

    /// Rummy-style melds: a list of kinds of meld (sets, runs), each with its own small form.
    void ShowMelds()
    {
        List<object?> types = Document.GetValue(["melds", "types"]) as List<object?> ?? [];
        for (int i = 0; i < types.Count; i++)
        {
            int index = i;
            Button remove = AddHeading($"Meld type {i + 1}", "One kind of meld, e.g. a set (same rank) or a run (same suit, numbers in order).", "trash")!;
            remove.TooltipText = $"Remove meld type {i + 1}";
            remove.Pressed += () => editor.Edit(() => Document.Remove(["melds", "types", index]));
            AddForm(["melds", "types", i], EditorFields.MeldType, editor.DeckColumns());
        }
        Button add = new()
        {
            ThemeTypeVariation = "FlatButton", Text = "Add meld type", Icon = EditorIcons.Icon("plus", Colors.White, 0.8f), SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
        };
        add.Pressed += () => editor.Edit(() =>
        {
            Dictionary<string, object?> newType = new() { ["same"] = "rank", ["min"] = 3L };
            if (Document.Has(["melds", "types"]))
            {
                Document.Insert(["melds", "types"], types.Count, newType);
            }
            else
            {
                Document.SetValue(["melds", "types"], new List<object?> { newType });
            }
        });
        content.AddChild(add);
        AddHeading("Deadwood", "Cards left out of melds.");
        AddForm(["melds"], EditorFields.Melds, editor.DeckColumns());
    }

    /// Name and starting value of each variable; values are written as in the file (5, "draw", true, []).
    /// Readouts (status) instead pair any name with an expression.
    void AddNameTable(string key, string title, string help, bool readouts = false)
    {
        Button add = AddHeading(title, help, "plus")!;
        add.TooltipText = readouts ? "Add a readout" : "Add a variable";
        add.Pressed += () => AddTableEntry(key, readouts);
        GridContainer grid = new() { Columns = 3 };
        foreach (string name in (Document.GetValue([key]) as Dictionary<string, object?>)?.Keys.ToList() ?? [])
        {
            LineEdit nameBox = new()
            {
                Text = name, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.7f, SizeFlagsVertical = SizeFlags.ShrinkBegin,
            };
            void Rename()
            {
                if (nameBox.Text != name && GoodTableName(key, nameBox.Text, readouts))
                {
                    editor.Edit(() => Document.RenameKey([key, name], nameBox.Text));
                }
            }
            nameBox.TextSubmitted += _ => Rename();
            nameBox.FocusExited += Rename;
            Control valueBox;
            if (readouts)
            {
                SmartField field = editor.SmartField(Values.Text(Document.GetValue([key, name])));
                field.Committed += text => editor.Change([key, name], text);
                valueBox = field;
            }
            else
            {
                LineEdit line = new() { Text = GameDocument.OneLine(Document.GetValue([key, name])), PlaceholderText = "starting value" };
                void Send() => editor.Change([key, name], ValueFromText(line.Text));
                line.TextSubmitted += _ => Send();
                line.FocusExited += Send;
                valueBox = line;
            }
            valueBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            Button remove = new() { ThemeTypeVariation = "FlatButton", Icon = EditorIcons.Icon("trash", Colors.White, 0.75f), TooltipText = "Remove " + name };
            remove.Pressed += () => editor.Edit(() => Document.Remove([key, name]));
            grid.AddChild(nameBox);
            grid.AddChild(valueBox);
            grid.AddChild(remove);
            if (name == nameToFocus)
            {
                nameToFocus = "";
                nameBox.CallDeferred(Control.MethodName.GrabFocus);
                nameBox.CallDeferred(LineEdit.MethodName.SelectAll);
            }
        }
        content.AddChild(grid);
    }

    /// Adds a variable (or readout) with a name not used yet, and puts the cursor in its name box.
    public void AddTableEntry(string key, bool readouts = false)
    {
        string name = UnusedName(key, readouts ? "Readout" : "variable");
        nameToFocus = name;
        editor.Edit(() => Document.SetValue([key, name], readouts ? "''" : 0L));
    }

    string UnusedName(string key, string baseName)
    {
        string name = baseName;
        for (int number = 2; Document.Has([key, name]); number++)
        {
            name = baseName.IsValidIdentifier() ? $"{baseName}_{number}" : $"{baseName} {number}";
        }
        return name;
    }

    /// Variables are used in steps, so their names must be single words; readouts are labels and can say anything.
    bool GoodTableName(string key, string name, bool readouts) =>
        (readouts ? name.Length > 0 : name.IsValidIdentifier()) && !Document.Has([key, name]);

    /// 5, true, [] and "text" as written in the file; anything that isn't valid there is taken as text.
    static object? ValueFromText(string text)
    {
        object? value = EditorForm.ParseOrNull(text);
        return value == null && text.Trim() != "null" ? text : value;
    }

    void AddForm(List<object> path, Field[] fields, List<string>? columns = null)
    {
        EditorForm form = new();
        form.ShowFields(editor, path, fields, columns);
        form.Changed += (changedPath, value) =>
        {
            bool existed = Document.Has(changedPath);
            editor.Change(changedPath, value);
            object last = changedPath[^1];
            // A drag or click being added or taken out greys out the other, so the form is built again.
            bool addedOrTakenOut = existed != Document.Has(changedPath) && last is "drag" or "click" or "double_click";
            if (value == null || last is "per_player" or "copies" or "cards" || addedOrTakenOut)
            {
                CallDeferred(MethodName.ShowPage);
            }
        };
        form.Described += (title, help) => Describe(title, help);
        content.AddChild(form);
    }

    /// A small heading, with its help in the description on hover. With an icon, it has a button on the right, which
    /// is returned.
    Button? AddHeading(string text, string help, string buttonIcon = "")
    {
        if (content.GetChildren().Any(child => !child.IsQueuedForDeletion()))
        {
            content.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        }
        HBoxContainer row = new();
        row.AddChild(new Label
        {
            Text = text.ToUpperInvariant(), ThemeTypeVariation = "HeaderLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = help,
            MouseFilter = MouseFilterEnum.Pass,
        });
        row.MouseEntered += () => Describe(text, help);
        content.AddChild(row);
        if (buttonIcon.Length == 0)
        {
            return null;
        }
        Button button = new() { ThemeTypeVariation = "FlatButton", Icon = EditorIcons.Icon(buttonIcon, Colors.White, 0.8f) };
        row.AddChild(button);
        return button;
    }

    /// The comment lines above an entry in the file, editable as a note; a small button until there is one.
    void AddNote(List<object> path)
    {
        TextEdit note = new()
        {
            PlaceholderText = "Note (kept as a // comment in the file)", Text = Document.Note(path), ScrollFitContentHeight = true,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        note.AddThemeColorOverride("font_color", EditorTheme.Comment);
        note.FocusExited += () =>
        {
            if (note.Text != Document.Note(path))
            {
                editor.Edit(() => Document.SetNote(path, note.Text));
            }
        };
        note.Visible = note.Text.Length > 0;
        content.AddChild(note);
        if (note.Visible)
        {
            return;
        }
        Button add = new()
        {
            ThemeTypeVariation = "FlatButton", Text = "Add note", Icon = EditorIcons.Icon("note", Colors.White, 0.75f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin, TooltipText = "A note about this, kept as a // comment in the game file",
        };
        add.AddThemeColorOverride("font_color", EditorTheme.TextDim);
        add.Pressed += () =>
        {
            add.Visible = false;
            note.Visible = true;
            note.GrabFocus();
        };
        content.AddChild(add);
        content.MoveChild(add, note.GetIndex());
    }
}

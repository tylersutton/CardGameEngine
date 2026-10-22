using Godot;

/// One entry in the outline: its key in the file, the name shown, and a word or two beside it.
public record OutlineEntry(string Key, string Label, string Detail = "");

/// One section of the outline, with its entries; Noun and Count for sections of named entries (zones, actions...).
public record OutlineSection(string Name, List<OutlineEntry> Entries, string Noun = "", int Count = 0);

/// The editor's outline: every part of the game, with its named entries (zones, actions...) under their section.
/// Picking a row sends Picked(section, entry). A section's + button, the right-click menu, F2 and Delete ask the
/// editor to add, rename, duplicate or remove entries; the editor makes the change and shows the outline again.
public partial class GameOutline : Tree
{
    public static readonly Dictionary<string, string> Icons = new()
    {
        ["Game"] = "game", ["Table"] = "table", ["Deck"] = "deck", ["Players"] = "players", ["Variables"] = "variables",
        ["Actions"] = "actions", ["Turn"] = "turn", ["Triggers"] = "triggers", ["Conditions"] = "conditions", ["Scoring"] = "scoring",
        ["Melds"] = "melds",
    };
    const int AddButton = 0;
    const int MenuRename = 0;
    const int MenuDuplicate = 1;
    const int MenuRemove = 2;
    const int MenuAdd = 3;

    public event Action<string, string>? Picked;
    public event Action<string>? AddAsked;
    public event Action<string, string, string>? RenameAsked;
    public event Action<string, string>? DuplicateAsked;
    public event Action<string, string>? RemoveAsked;

    public string Filter { get; set; } = "";

    readonly PopupMenu menu = new();
    /// Sections the user folded away, kept when the outline is shown again.
    readonly Dictionary<string, bool> folded = [];
    /// While the editor is filling or selecting rows, so that doesn't count as the user picking one.
    bool quiet;

    public GameOutline()
    {
        HideRoot = true;
        Columns = 2;
        SetColumnExpand(1, false);
        SetColumnCustomMinimumWidth(1, 64);
        SelectMode = SelectModeEnum.Row;
        AllowRmbSelect = true;
        ScrollHorizontalEnabled = false;
        ItemSelected += OnSelected;
        ItemCollapsed += item =>
        {
            if (!quiet && item.GetParent() == GetRoot())
            {
                folded[Where(item).Section] = item.Collapsed;
            }
        };
        ButtonClicked += (item, _, id, _) =>
        {
            if (id == AddButton)
            {
                AddAsked?.Invoke(Where(item).Section);
            }
        };
        ItemMouseSelected += (at, button) =>
        {
            if (button == (long)MouseButton.Right)
            {
                OpenMenu(GetGlobalTransform() * at);
            }
        };
        ItemEdited += OnRenamed;
        menu.IdPressed += OnMenu;
        AddChild(menu);
    }

    /// problems: "section/entry" (or "section") -> what's wrong there.
    public void ShowSections(List<OutlineSection> sections, Dictionary<string, string> problems, string section, string entry)
    {
        quiet = true;
        Clear();
        TreeItem root = CreateItem();
        string lowered = Filter.ToLowerInvariant();
        foreach (OutlineSection part in sections)
        {
            TreeItem item = CreateItem(root);
            item.SetText(0, part.Name);
            item.SetIcon(0, EditorIcons.Icon(Icons[part.Name]));
            item.SetIconMaxWidth(0, 16);
            item.SetIconModulate(0, EditorTheme.TextDim);
            item.SetMetadata(0, new Godot.Collections.Array { part.Name, "" });
            if (part.Count > 0)
            {
                item.SetText(1, part.Count.ToString());
                item.SetCustomColor(1, EditorTheme.TextDim);
                item.SetTextAlignment(1, HorizontalAlignment.Right);
            }
            if (part.Noun.Length > 0)
            {
                item.AddButton(1, EditorIcons.Icon("plus", Colors.White, 0.85f), AddButton, false, "Add a " + part.Noun);
            }
            bool matchesSection = lowered.Length == 0 || part.Name.ToLowerInvariant().Contains(lowered);
            bool anyEntryMatches = false;
            foreach (OutlineEntry row in part.Entries)
            {
                TreeItem child = CreateItem(item);
                child.SetText(0, row.Label);
                child.SetMetadata(0, new Godot.Collections.Array { part.Name, row.Key });
                if (row.Detail.Length > 0)
                {
                    child.SetText(1, row.Detail);
                    child.SetCustomColor(1, EditorTheme.TextDim);
                    child.SetTextAlignment(1, HorizontalAlignment.Right);
                }
                if (problems.TryGetValue(part.Name + "/" + row.Key, out string? entryProblem))
                {
                    Mark(child, entryProblem);
                }
                child.Visible = matchesSection || row.Label.ToLowerInvariant().Contains(lowered);
                anyEntryMatches |= child.Visible;
                if (part.Name == section && row.Key == entry)
                {
                    child.Select(0);
                }
            }
            item.Visible = matchesSection || anyEntryMatches;
            item.Collapsed = folded.GetValueOrDefault(part.Name) && lowered.Length == 0;
            if (problems.TryGetValue(part.Name, out string? problem))
            {
                Mark(item, problem);
            }
            if (part.Name == section && entry.Length == 0)
            {
                item.Select(0);
            }
        }
        quiet = false;
    }

    /// Selects the row without sending Picked.
    public void ShowSelected(string section, string entry)
    {
        quiet = true;
        DeselectAll();
        foreach (TreeItem item in GetRoot()?.GetChildren() ?? [])
        {
            if (Where(item).Section != section)
            {
                continue;
            }
            TreeItem target = item.GetChildren().LastOrDefault(child => Where(child).Entry == entry) ?? item;
            if (target != item)
            {
                item.Collapsed = false;
            }
            target.Select(0);
            ScrollToItem(target);
        }
        quiet = false;
    }

    /// Starts renaming the selected entry in place.
    public void StartRename()
    {
        TreeItem? item = GetSelected();
        if (item != null && Where(item).Entry.Length > 0 && CanEdit(item))
        {
            item.SetEditable(0, true);
            EditSelected(true);
        }
    }

    static (string Section, string Entry) Where(TreeItem item)
    {
        Godot.Collections.Array where = item.GetMetadata(0).AsGodotArray();
        return (where[0].AsString(), where[1].AsString());
    }

    static void Mark(TreeItem item, string problem)
    {
        item.SetCustomColor(0, EditorTheme.Error);
        item.SetTooltipText(0, problem);
    }

    void OnSelected()
    {
        if (!quiet)
        {
            (string section, string entry) = Where(GetSelected());
            Picked?.Invoke(section, entry);
        }
    }

    void OnRenamed()
    {
        TreeItem item = GetEdited();
        item.SetEditable(0, false);
        (string section, string entry) = Where(item);
        string newName = item.GetText(0).Trim();
        if (newName != entry)
        {
            RenameAsked?.Invoke(section, entry, newName);
        }
    }

    bool CanEdit(TreeItem item)
    {
        TreeItem sectionItem = item.GetParent() == GetRoot() ? item : item.GetParent();
        return sectionItem.GetButtonCount(1) > 0;
    }

    public override void _GuiInput(InputEvent @event)
    {
        TreeItem? item = GetSelected();
        if (@event is not InputEventKey { Pressed: true } key || item == null)
        {
            return;
        }
        (string section, string entry) = Where(item);
        if (key.Keycode == Key.F2)
        {
            StartRename();
            AcceptEvent();
        }
        else if (key.Keycode == Key.Delete && entry.Length > 0 && CanEdit(item))
        {
            RemoveAsked?.Invoke(section, entry);
            AcceptEvent();
        }
    }

    void OpenMenu(Vector2 at)
    {
        TreeItem? item = GetSelected();
        if (item == null || !CanEdit(item))
        {
            return;
        }
        menu.Clear();
        if (Where(item).Entry.Length > 0)
        {
            menu.AddIconItem(EditorIcons.Icon("rename"), "Rename", MenuRename, Key.F2);
            menu.AddIconItem(EditorIcons.Icon("duplicate"), "Duplicate", MenuDuplicate);
            menu.AddSeparator();
            menu.AddIconItem(EditorIcons.Icon("trash"), "Delete", MenuRemove, Key.Delete);
        }
        else
        {
            menu.AddIconItem(EditorIcons.Icon("plus"), "Add", MenuAdd);
        }
        menu.ResetSize();
        menu.Popup(new Rect2I((Vector2I)at, Vector2I.Zero));
    }

    void OnMenu(long id)
    {
        (string section, string entry) = Where(GetSelected());
        switch (id)
        {
            case MenuRename:
                StartRename();
                break;
            case MenuDuplicate:
                DuplicateAsked?.Invoke(section, entry);
                break;
            case MenuRemove:
                RemoveAsked?.Invoke(section, entry);
                break;
            case MenuAdd:
                AddAsked?.Invoke(section);
                break;
        }
    }
}

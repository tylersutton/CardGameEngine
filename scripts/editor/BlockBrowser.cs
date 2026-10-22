using Editing;
using Godot;

/// The sidebar's Building blocks page: every building block by kind (from BlockHelp), searchable, with what it does.
/// Double-click or Insert puts it into the step being typed (InsertAsked).
public partial class BlockBrowser : VBoxContainer
{
    public event Action<string>? InsertAsked;

    readonly LineEdit search = new() { PlaceholderText = "Search building blocks", ClearButtonEnabled = true };
    readonly Tree tree = new() { HideRoot = true, SizeFlagsVertical = SizeFlags.ExpandFill };
    readonly Label description = new() { ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };

    static BlockHelp Help => EditorScreen.Help;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);
        search.RightIcon = EditorIcons.Icon("search", EditorTheme.TextDim);
        search.TextChanged += _ => Fill();
        AddChild(EditorTheme.Padded(search, 8, 0));
        tree.ItemSelected += ShowEntry;
        tree.ItemActivated += Insert;
        AddChild(tree);
        Button insert = new()
        {
            Text = "Insert into step", Icon = EditorIcons.Icon("plus", Colors.White, 0.85f),
            TooltipText = "Put it where you were typing a step (double-click does the same)",
        };
        insert.Pressed += Insert;
        VBoxContainer details = new();
        details.AddChild(description);
        details.AddChild(insert);
        AddChild(EditorTheme.Padded(details, 10, 8));
        Fill();
    }

    void Fill()
    {
        tree.Clear();
        TreeItem root = tree.CreateItem();
        string typed = search.Text.ToLowerInvariant();
        Dictionary<string, TreeItem> categories = [];
        for (int i = 0; i < Help.Entries.Count; i++)
        {
            BlockHelp.Entry entry = Help.Entries[i];
            if (typed.Length > 0 && !entry.Title.ToLowerInvariant().Contains(typed) && !entry.Text.ToLowerInvariant().Contains(typed))
            {
                continue;
            }
            if (!categories.TryGetValue(entry.Category, out TreeItem? heading))
            {
                heading = tree.CreateItem(root);
                heading.SetText(0, entry.Category);
                heading.SetSelectable(0, false);
                heading.SetCustomColor(0, EditorTheme.TextDim);
                categories[entry.Category] = heading;
            }
            TreeItem item = tree.CreateItem(heading);
            item.SetText(0, entry.Title);
            item.SetIcon(0, EditorIcons.Icon("function", EditorTheme.Function, 0.85f));
            item.SetCustomFont(0, EditorTheme.CodeFont);
            item.SetTooltipText(0, entry.Text);
            item.SetMetadata(0, i);
        }
        description.Text = Help.Entries.Count > 0 ? "Pick a building block to see what it does." : "The building blocks are listed in docs/reference.md.";
    }

    BlockHelp.Entry? Selected() => tree.GetSelected() is { } item && item.GetMetadata(0).VariantType == Variant.Type.Int ? Help.Entries[item.GetMetadata(0).AsInt32()] : null;

    void ShowEntry()
    {
        if (Selected() is { } entry)
        {
            description.Text = entry.Title + "\n" + entry.Text;
        }
    }

    void Insert()
    {
        if (Selected() is { } entry)
        {
            InsertAsked?.Invoke(entry.Names[0] + "(");
        }
    }
}

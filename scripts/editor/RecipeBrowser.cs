using Editing;
using Godot;

/// The sidebar's Recipes page: ready-made pieces of a game (Recipes), what each one does, and a button to add the
/// chosen one to the game.
public partial class RecipeBrowser : VBoxContainer
{
    public event Action<string>? AddAsked;

    readonly ItemList list = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    readonly Label description = new() { ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    List<string> paths = [];

    public override void _Ready()
    {
        paths = Recipes.Paths(ProjectFiles.With());
        AddThemeConstantOverride("separation", 6);
        foreach (string path in paths)
        {
            list.AddItem(Recipes.Title(path), EditorIcons.Icon("recipes", EditorTheme.TextDim));
        }
        list.ItemSelected += index => ShowRecipe((int)index);
        list.ItemActivated += index => AddAsked?.Invoke(paths[(int)index]);
        Button add = new() { ThemeTypeVariation = "AccentButton", Text = "Add to game", Icon = EditorIcons.Icon("plus", Colors.White, 0.85f) };
        add.Pressed += () =>
        {
            if (list.GetSelectedItems().Length > 0)
            {
                AddAsked?.Invoke(paths[list.GetSelectedItems()[0]]);
            }
        };
        VBoxContainer details = new();
        details.AddChild(description);
        details.AddChild(add);
        AddChild(list);
        AddChild(EditorTheme.Padded(details, 10, 8));
        if (paths.Count > 0)
        {
            list.Select(0);
            ShowRecipe(0);
        }
    }

    void ShowRecipe(int index)
    {
        description.Text = Recipes.Description(ProjectFiles.With(), paths[index]);
    }
}

using Core;
using Godot;

/// The editor's deck: a table of cards (one row per kind of card, one column per CSV column) with the selected card
/// drawn beside it using the game's card face. Every change is sent as Edited(csvText).
public partial class DeckTable : VBoxContainer
{
    const float PreviewScale = 2;

    public event Action<string>? Edited;

    /// Where the card is drawn (the editor gives it the game's own theme).
    public Control PreviewSpot { get; } = new() { CustomMinimumSize = CardView.CardSize * PreviewScale + new Vector2(20, 20) };

    List<List<string>> rows = [];
    Dictionary<string, object?> cardFace = [];
    readonly Tree grid = new()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill, HideRoot = true, ColumnTitlesVisible = true, SelectMode = Tree.SelectModeEnum.Single,
    };
    CardView? preview;

    public DeckTable()
    {
        AddThemeConstantOverride("separation", 0);
        PanelContainer bar = new() { ThemeTypeVariation = "Bar" };
        HBoxContainer buttons = new();
        AddButton(buttons, "Card", "plus", "Add a card", AddCard);
        AddButton(buttons, "", "duplicate", "Duplicate the selected card", DuplicateCard);
        AddButton(buttons, "", "trash", "Remove the selected card", RemoveCard);
        buttons.AddChild(new VSeparator());
        AddButton(buttons, "Column", "plus", "Add a column", () => Ask("New column", "", AddColumn));
        AddButton(buttons, "", "rename", "Rename the selected column", RenameSelectedColumn);
        AddButton(buttons, "", "trash", "Remove the selected column", RemoveColumn);
        bar.AddChild(buttons);
        AddChild(bar);
        HBoxContainer body = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
        grid.ItemEdited += OnCellEdited;
        grid.CellSelected += ShowPreview;
        body.AddChild(grid);
        body.AddChild(PreviewSpot);
        AddChild(body);
    }

    static void AddButton(HBoxContainer buttons, string text, string icon, string tooltip, Action pressed)
    {
        Button button = new() { ThemeTypeVariation = "FlatButton", Text = text, Icon = EditorIcons.Icon(icon, Colors.White, 0.85f), TooltipText = tooltip };
        button.Pressed += pressed;
        buttons.AddChild(button);
    }

    public void ShowDeck(string csvText, Dictionary<string, object?> face)
    {
        rows = DeckLoader.Rows(csvText);
        cardFace = face;
        Fill();
    }

    void Fill()
    {
        grid.Clear();
        if (rows.Count == 0)
        {
            rows.Add(["name"]);
        }
        List<string> headers = rows[0];
        grid.Columns = headers.Count;
        for (int column = 0; column < headers.Count; column++)
        {
            grid.SetColumnTitle(column, headers[column]);
            grid.SetColumnExpand(column, true);
        }
        TreeItem root = grid.CreateItem();
        foreach (List<string> row in rows.Skip(1))
        {
            TreeItem item = grid.CreateItem(root);
            for (int column = 0; column < headers.Count; column++)
            {
                item.SetText(column, column < row.Count ? row[column] : "");
                item.SetEditable(column, true);
            }
        }
        ShowPreview();
    }

    static int RowOf(TreeItem? item) => item != null ? item.GetIndex() + 1 : -1;

    void OnCellEdited()
    {
        TreeItem item = grid.GetEdited();
        int column = grid.GetEditedColumn();
        List<string> row = rows[RowOf(item)];
        while (row.Count < rows[0].Count)
        {
            row.Add("");
        }
        row[column] = item.GetText(column);
        Send();
    }

    void AddCard()
    {
        rows.Add(Enumerable.Repeat("", rows[0].Count).ToList());
        Fill();
        Send();
    }

    void DuplicateCard()
    {
        int row = RowOf(grid.GetSelected());
        if (row > 0)
        {
            rows.Insert(row + 1, [.. rows[row]]);
            Fill();
            Send();
        }
    }

    void RemoveCard()
    {
        int row = RowOf(grid.GetSelected());
        if (row > 0)
        {
            rows.RemoveAt(row);
            Fill();
            Send();
        }
    }

    void AddColumn(string name)
    {
        if (name.Length == 0 || rows[0].Contains(name))
        {
            return;
        }
        for (int row = 0; row < rows.Count; row++)
        {
            rows[row].Add(row == 0 ? name : "");
        }
        Fill();
        Send();
    }

    void RenameSelectedColumn()
    {
        int column = grid.GetSelectedColumn();
        if (column < 0)
        {
            return;
        }
        Ask("Rename column", rows[0][column], name =>
        {
            if (name.Length > 0 && !rows[0].Contains(name))
            {
                rows[0][column] = name;
                Fill();
                Send();
            }
        });
    }

    void RemoveColumn()
    {
        int column = grid.GetSelectedColumn();
        if (column < 0 || rows[0].Count <= 1)
        {
            return;
        }
        foreach (List<string> row in rows.Where(row => column < row.Count))
        {
            row.RemoveAt(column);
        }
        Fill();
        Send();
    }

    void Send()
    {
        ShowPreview();
        Edited?.Invoke(DeckLoader.ToCsv(rows));
    }

    /// The selected card (or the first) drawn with the game's card face.
    void ShowPreview()
    {
        preview?.QueueFree();
        preview = null;
        int row = Math.Max(RowOf(grid.GetSelected()), 1);
        if (row >= rows.Count)
        {
            return;
        }
        Dictionary<string, string> data = [];
        for (int column = 0; column < rows[0].Count; column++)
        {
            data[rows[0][column]] = column < rows[row].Count ? rows[row][column] : "";
        }
        preview = new(new Card(data), cardFace)
        {
            Scale = new Vector2(PreviewScale, PreviewScale), PivotOffset = Vector2.Zero, Position = new Vector2(10, 10), MouseFilter = MouseFilterEnum.Ignore,
        };
        PreviewSpot.AddChild(preview);
    }

    void Ask(string title, string start, Action<string> then)
    {
        ConfirmationDialog dialog = new() { Title = title };
        LineEdit line = new() { Text = start };
        dialog.AddChild(line);
        dialog.RegisterTextEnter(line);
        dialog.Confirmed += () => then(line.Text.Trim());
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(320, 80));
        line.GrabFocus();
    }
}

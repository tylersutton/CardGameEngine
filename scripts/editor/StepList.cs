using Editing;
using Godot;

/// A list of steps in the editor: plain steps, and "if", "for each" and "repeat while" blocks holding their own steps
/// (folded away with their arrow) behind an indent line. Steps can be added, moved, copied and removed, or the whole
/// list edited as text. Edits go straight to the editor, one step at a time, so comments elsewhere in the list survive.
public partial class StepList : VBoxContainer
{
    const int IndentWidth = 14;
    static readonly string[] AddChoices = ["Step", "If", "For each", "Repeat while"];

    readonly EditorScreen editor;
    readonly List<object> path;

    GameDocument Document => editor.Document;

    public StepList(EditorScreen gameEditor, List<object> listPath)
    {
        editor = gameEditor;
        path = listPath;
        object? value = Document.GetValue(path);
        List<object?> steps = value as List<object?> ?? (value != null ? [value] : []);
        for (int i = 0; i < steps.Count; i++)
        {
            AddChild(Row(steps[i], StepPath(value, i), i, steps.Count));
        }
        HBoxContainer footer = new();
        MenuButton add = new() { Text = "Add step", Icon = EditorIcons.Icon("plus", Colors.White, 0.8f), ThemeTypeVariation = "FlatButton" };
        add.AddThemeColorOverride("font_color", EditorTheme.TextDim);
        foreach (string choice in AddChoices)
        {
            add.GetPopup().AddItem(choice);
        }
        add.GetPopup().IndexPressed += index => Add(NewStep((int)index));
        footer.AddChild(add);
        Button asText = new() { Text = "Edit as text", Icon = EditorIcons.Icon("file", Colors.White, 0.8f), ThemeTypeVariation = "FlatButton" };
        asText.AddThemeColorOverride("font_color", EditorTheme.TextDim);
        asText.Pressed += EditAsText;
        footer.AddChild(asText);
        AddChild(footer);
    }

    /// A fresh step of each kind the Add menu offers.
    static object NewStep(int choice) => choice switch
    {
        1 => new Dictionary<string, object?> { ["if"] = "", ["then"] = new List<object?>() },
        2 => new Dictionary<string, object?> { ["for"] = "item", ["in"] = "", ["do"] = new List<object?>() },
        3 => new Dictionary<string, object?> { ["while"] = "", ["do"] = new List<object?>() },
        _ => "",
    };

    /// A list written as a single step ("then": "end_turn()") has just that step, at the list's own path.
    List<object> StepPath(object? value, int index) => value is string ? path : [.. path, index];

    Control Row(object? step, List<object> stepPath, int index, int count)
    {
        if (step is not Dictionary<string, object?> block)
        {
            return Line([], Field(step as string ?? "", stepPath), stepPath, index, count);
        }
        VBoxContainer box = new();
        VBoxContainer inner = new();
        if (block.TryGetValue("for", out object? itemName))
        {
            LineEdit name = new() { Text = itemName as string ?? "", CustomMinimumSize = new Vector2(70, 0) };
            name.TextSubmitted += text => editor.Change([.. stepPath, "for"], text);
            name.FocusExited += () =>
            {
                if (name.Text != (itemName as string ?? ""))
                {
                    editor.Change([.. stepPath, "for"], name.Text);
                }
            };
            box.AddChild(Line([Word("for each"), name, Word("in")], Field(block.GetValueOrDefault("in") as string ?? "", [.. stepPath, "in"]), stepPath, index, count));
            inner.AddChild(Nested([.. stepPath, "do"]));
        }
        else if (block.TryGetValue("while", out object? condition))
        {
            box.AddChild(Line([Word("repeat while")], Field(condition as string ?? "", [.. stepPath, "while"]), stepPath, index, count));
            inner.AddChild(Nested([.. stepPath, "do"]));
        }
        else
        {
            box.AddChild(Line([Word("if")], Field(block.GetValueOrDefault("if") as string ?? "", [.. stepPath, "if"]), stepPath, index, count));
            inner.AddChild(Nested([.. stepPath, "then"]));
            if (block.ContainsKey("else"))
            {
                inner.AddChild(Word("else"));
                inner.AddChild(Nested([.. stepPath, "else"]));
            }
            else
            {
                Button addElse = new()
                {
                    Text = "else", Icon = EditorIcons.Icon("plus", Colors.White, 0.7f), ThemeTypeVariation = "FlatButton",
                    SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
                };
                addElse.AddThemeColorOverride("font_color", EditorTheme.TextDim);
                addElse.Pressed += () => editor.Edit(() => Document.SetValue([.. stepPath, "else"], new List<object?>()));
                inner.AddChild(addElse);
            }
        }
        PanelContainer guide = new();
        guide.AddThemeStyleboxOverride("panel", EditorTheme.Box(Colors.Transparent, EditorTheme.InputBorder, [1, 0, 0, 0], 0, new Vector2(6, 0)));
        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", IndentWidth);
        margin.AddChild(guide);
        guide.AddChild(inner);
        Button fold = new() { ThemeTypeVariation = "FlatButton", Icon = EditorIcons.Icon("chevron_down", Colors.White, 0.8f), FocusMode = FocusModeEnum.None };
        fold.Pressed += () =>
        {
            margin.Visible = !margin.Visible;
            fold.Icon = EditorIcons.Icon(margin.Visible ? "chevron_down" : "chevron_right", Colors.White, 0.8f);
        };
        Node firstLine = box.GetChild(0);
        firstLine.AddChild(fold);
        firstLine.MoveChild(fold, 0);
        box.AddChild(margin);
        return box;
    }

    StepList Nested(List<object> listPath) => new(editor, listPath);

    SmartField Field(string text, List<object> fieldPath)
    {
        SmartField field = editor.SmartField(text);
        field.Committed += newText => editor.Change(fieldPath, newText);
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return field;
    }

    /// One line: its words, its text box, and a menu to move, copy or remove the step.
    HBoxContainer Line(List<Control> parts, Control field, List<object> stepPath, int index, int count)
    {
        HBoxContainer line = new();
        foreach (Control part in parts)
        {
            line.AddChild(part);
        }
        line.AddChild(field);
        MenuButton menu = new() { ThemeTypeVariation = "FlatButton", Icon = EditorIcons.Icon("more", Colors.White, 0.8f), TooltipText = "Move, copy or remove this step" };
        PopupMenu popup = menu.GetPopup();
        popup.AddIconItem(EditorIcons.Icon("up"), "Move up");
        popup.AddIconItem(EditorIcons.Icon("down"), "Move down");
        popup.AddIconItem(EditorIcons.Icon("duplicate"), "Duplicate");
        popup.AddIconItem(EditorIcons.Icon("trash"), "Remove");
        popup.SetItemDisabled(0, index == 0);
        popup.SetItemDisabled(1, index == count - 1);
        popup.IndexPressed += choice => OnStepMenu((int)choice, index, stepPath);
        line.AddChild(menu);
        return line;
    }

    void OnStepMenu(int choice, int index, List<object> stepPath)
    {
        switch (choice)
        {
            case 0:
            case 1:
                Move(index, choice == 0 ? index - 1 : index + 1);
                break;
            case 2:
                Copy(index);
                break;
            case 3:
                editor.Edit(() => Document.Remove(stepPath));
                break;
        }
    }

    static Label Word(string text)
    {
        Label label = new() { Text = text };
        label.AddThemeFontOverride("font", EditorTheme.CodeFont);
        label.AddThemeColorOverride("font_color", EditorTheme.Keyword);
        return label;
    }

    /// The list is read inside Edit, after any text still being typed has been sent.
    internal void Add(object step)
    {
        editor.Edit(() =>
        {
            object? value = Document.GetValue(path);
            if (value is string single)
            {
                Document.SetValue(path, new List<object?> { single, step });
            }
            else
            {
                Document.Insert(path, value is List<object?> list ? list.Count : 0, step);
            }
        });
    }

    internal void Move(int from, int to)
    {
        editor.Edit(() =>
        {
            object? step = Document.GetValue([.. path, from]);
            Document.Remove([.. path, from]);
            Document.Insert(path, to, step);
        });
    }

    void Copy(int index)
    {
        editor.Edit(() =>
        {
            object? value = Document.GetValue(path);
            if (value is string single)
            {
                Document.SetValue(path, new List<object?> { single, single });
            }
            else
            {
                Document.Insert(path, index + 1, ((List<object?>)value!)[index]);
            }
        });
    }

    /// The whole list as JSONC text, for when a form is more trouble than typing.
    void EditAsText()
    {
        ConfirmationDialog dialog = new() { Title = "Steps as text" };
        CodeEdit box = new()
        {
            CustomMinimumSize = new Vector2(640, 360), GuttersDrawLineNumbers = true,
            Text = GameDocument.Format(Document.GetValue(path, new List<object?>())),
        };
        dialog.AddChild(box);
        dialog.Confirmed += () =>
        {
            object? steps = EditorForm.ParseOrNull(box.Text);
            if (steps is List<object?> or string)
            {
                editor.Edit(() => Document.SetValue(path, steps));
            }
        };
        editor.AddChild(dialog);
        dialog.PopupCentered();
    }
}

using System.Text.Json;
using Core;
using Editing;
using Godot;

/// A form for one part of a game file (a zone, the card face...), built from EditorFields: one row per setting, its
/// name on the left and its control on the right, with grouped settings folded away under their heading. Each change
/// is sent as Changed(path, value); a value of null means "take the key out" (back to its default). Hovering or
/// focusing a row sends Described(key, help), for the inspector's description.
public partial class EditorForm : VBoxContainer
{
    const string DecksFolder = "res://decks/";
    const string NotSet = "(default)";
    /// Expression settings that may also be a plain number.
    static readonly string[] NumberOrText = ["pick", "up_to"];
    /// Kinds whose control takes the whole width, under the setting's name.
    static readonly string[] WideKinds = ["long_text", "json", "expression", "steps", "drag", "board"];
    /// The keys that say an action is done by clicking.
    static readonly string[] ClickKeys = ["click", "double_click"];

    /// Groups opened or closed by the user, by form and heading, kept while the editor is open.
    static readonly Dictionary<string, bool> openedGroups = [];

    public event Action<List<object>, object?>? Changed;
    public event Action<string, string>? Described;

    EditorScreen editor = null!;
    List<object> basePath = [];
    /// The deck's column names, for "column" fields.
    List<string> columns = [];

    GameDocument Document => editor.Document;

    public void ShowFields(EditorScreen gameEditor, List<object> path, Field[] fields, List<string>? deckColumns = null)
    {
        editor = gameEditor;
        basePath = path;
        columns = deckColumns ?? [];
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }
        foreach (Field field in fields.Where(field => field.Group.Length == 0))
        {
            AddChild(Row(field));
        }
        foreach (IGrouping<string, Field> group in fields.Where(field => field.Group.Length > 0).GroupBy(field => field.Group))
        {
            AddGroup(group.Key, group.ToList());
        }
    }

    /// JSONC text as plain values, or null if it doesn't read.
    public static object? ParseOrNull(string text)
    {
        try
        {
            return GameRules.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// A heading that folds its settings away; it starts open if any of them is set.
    void AddGroup(string group, List<Field> fields)
    {
        bool anySet = fields.Any(field => Document.Has([.. basePath, field.Key]));
        string remembered = (basePath.Count > 0 ? basePath[0] : "") + "/" + group;
        Button heading = new()
        {
            ThemeTypeVariation = "GroupButton", Text = group.ToUpperInvariant(), Alignment = HorizontalAlignment.Left, ToggleMode = true,
            ButtonPressed = openedGroups.GetValueOrDefault(remembered, anySet),
        };
        VBoxContainer rows = new();
        foreach (Field field in fields)
        {
            rows.AddChild(Row(field));
        }
        void ShowRows(bool open)
        {
            rows.Visible = open;
            heading.Icon = EditorIcons.Icon(open ? "chevron_down" : "chevron_right", Colors.White, 0.8f);
        }
        ShowRows(heading.ButtonPressed);
        heading.Toggled += open =>
        {
            openedGroups[remembered] = open;
            ShowRows(open);
        };
        AddChild(heading);
        AddChild(rows);
    }

    /// One setting: its name (bright when it's set in the file) and its control, and a button to take it out.
    Control Row(Field field)
    {
        List<object> path = [.. basePath, field.Key];
        bool isSet = Document.Has(path);
        Label nameLabel = new() { Text = field.Key, ClipText = true, TooltipText = field.Help, MouseFilter = MouseFilterEnum.Pass };
        nameLabel.AddThemeColorOverride("font_color", isSet ? EditorTheme.Text : EditorTheme.TextDim);
        Button reset = new()
        {
            ThemeTypeVariation = "FlatButton", Icon = EditorIcons.Icon("restart", Colors.White, 0.75f), TooltipText = "Take this out (back to the default)",
            Modulate = new Color(1, 1, 1, isSet ? 1 : 0), Disabled = !isSet, FocusMode = FocusModeEnum.None,
        };
        reset.Pressed += () => Changed?.Invoke(path, null);
        Control control = ControlFor(field, path, Document.GetValue(path));
        Container row;
        nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (WideKinds.Contains(field.Kind))
        {
            row = new VBoxContainer();
            HBoxContainer heading = new();
            heading.AddChild(nameLabel);
            heading.AddChild(reset);
            row.AddChild(heading);
            row.AddChild(control);
        }
        else
        {
            row = new HBoxContainer();
            nameLabel.SizeFlagsStretchRatio = 0.7f;
            control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(nameLabel);
            row.AddChild(control);
            row.AddChild(reset);
        }
        row.MouseEntered += () => Described?.Invoke(field.Key, field.Help);
        OnFocus(control, () => Described?.Invoke(field.Key, field.Help));
        return row;
    }

    /// Calls back when the control, or any box inside it, takes the keyboard focus.
    static void OnFocus(Control control, Action then)
    {
        if (control.FocusMode != FocusModeEnum.None)
        {
            control.FocusEntered += then;
        }
        foreach (Control child in control.GetChildren().OfType<Control>())
        {
            OnFocus(child, then);
        }
    }

    Control ControlFor(Field field, List<object> path, object? value)
    {
        switch (field.Kind)
        {
            case "long_text":
                TextEdit box = new() { Text = value != null ? Values.Text(value) : "", CustomMinimumSize = new Vector2(0, 54), WrapMode = TextEdit.LineWrappingMode.Boundary };
                box.FocusExited += () => Changed?.Invoke(path, box.Text.Length > 0 ? box.Text : null);
                return box;
            case "number":
            case "whole":
                SpinBox spin = new()
                {
                    AllowGreater = true, AllowLesser = true, Step = field.Kind == "whole" ? 1 : 0.01,
                    Value = Values.IsNumber(value) ? Values.ToDouble(value) : 0,
                };
                spin.ValueChanged += number => Changed?.Invoke(path, field.Kind == "whole" ? (long)number : Math.Round(number, 3));
                return spin;
            case "yes_no":
                CheckBox check = new() { Text = Values.IsTrue(value) ? "yes" : "no", ButtonPressed = value is true };
                check.Toggled += on =>
                {
                    check.Text = on ? "yes" : "no";
                    Changed?.Invoke(path, on);
                };
                return check;
            case "choice":
            case "column":
            case "deck":
                return Choice(path, value, Options(field));
            case "spot":
                return Spot(path, value);
            case "columns":
            case "list":
            case "names":
            case "numbers":
                LineEdit line = new()
                {
                    Text = value is List<object?> items ? string.Join(", ", items.Select(AsText)) : AsText(value),
                    PlaceholderText = "comma-separated" + (field.Kind == "columns" && columns.Count > 0 ? ": " + string.Join(", ", columns) : ""),
                };
                void Commit()
                {
                    List<object?> entries = SplitNames(line.Text).Select(item => field.Kind == "numbers" ? (object?)Values.ToInt(item) : item).ToList();
                    bool oneAllowed = field.Kind == "list" && entries.Count == 1;
                    Changed?.Invoke(path, entries.Count == 0 ? null : oneAllowed ? entries[0] : entries);
                }
                line.TextSubmitted += _ => Commit();
                line.FocusExited += Commit;
                return line;
            case "json":
                return JsonBox(path, value, field.Example);
            case "expression":
                SmartField smart = editor.SmartField(AsText(value));
                smart.Committed += text => Changed?.Invoke(path, NumberOrText.Contains(field.Key) ? NumberOrTextValue(text) : text.Length > 0 ? text : null);
                return smart;
            case "steps":
                return new StepList(editor, path);
            case "drag":
                return Drag(path, value);
            case "click":
                return Click(path, value);
            case "board":
                return Board(path, value);
        }
        LineEdit textLine = new() { Text = value != null ? Values.Text(value) : "" };
        void Submit() => Changed?.Invoke(path, textLine.Text.Length > 0 ? textLine.Text : null);
        textLine.TextSubmitted += _ => Submit();
        textLine.FocusExited += Submit;
        return textLine;
    }

    static string AsText(object? value) => value switch
    {
        null => "",
        double or long or List<object?> => GameDocument.OneLine(value),
        _ => Values.Text(value),
    };

    static List<string> SplitNames(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(name => name.Trim()).ToList();

    /// "3" is the number 3 (for pick and up_to); anything else stays text. Empty takes the key out.
    static object? NumberOrTextValue(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }
        return Values.IsValidFloat(text) ? Values.ToFloat(text) : text;
    }

    List<string> Options(Field field) => field.Kind switch
    {
        "column" => columns,
        "deck" => DirAccess.GetFilesAt(DecksFolder).Where(file => file.GetExtension() == "csv").Select(file => DecksFolder + file).ToList(),
        _ => [.. field.Options ?? []],
    };

    OptionButton Choice(List<object> path, object? value, List<string> options)
    {
        OptionButton picker = new() { FitToLongestItem = false, ClipText = true };
        picker.AddItem(NotSet);
        string current = value != null ? Values.Text(value) : "";
        List<string> all = value != null && !options.Contains(current) ? [.. options, current] : options;
        foreach (string option in all)
        {
            // Decks are listed by file name.
            picker.AddItem(option.StartsWith("res://") ? option.GetFile() : option);
            picker.SetItemMetadata(picker.ItemCount - 1, option);
            picker.SetItemTooltip(picker.ItemCount - 1, option);
            if (value != null && option == current)
            {
                picker.Select(picker.ItemCount - 1);
            }
        }
        picker.ItemSelected += index => Changed?.Invoke(path, index == 0 ? null : picker.GetItemMetadata((int)index).AsString());
        return picker;
    }

    /// Two numbers from 0 to 1: across and down the table.
    HBoxContainer Spot(List<object> path, object? value)
    {
        HBoxContainer row = new();
        List<SpinBox> spins = [];
        string[] tooltips = ["across, from 0 (left) to 1 (right)", "down, from 0 (top) to 1 (bottom)"];
        for (int i = 0; i < 2; i++)
        {
            SpinBox spin = new()
            {
                MinValue = -1, MaxValue = 2, Step = 0.005, Prefix = i == 0 ? "x" : "y", TooltipText = tooltips[i],
                Value = value is List<object?> { Count: 2 } spot ? Values.ToDouble(spot[i]) : 0.5, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            spins.Add(spin);
            row.AddChild(spin);
        }
        foreach (SpinBox spin in spins)
        {
            spin.ValueChanged += _ => Changed?.Invoke(path, new List<object?> { Math.Round(spins[0].Value, 3), Math.Round(spins[1].Value, 3) });
        }
        return row;
    }

    /// A drag's "from" and "to": zone names (comma-separated for several), each left empty for any zone, and whether a
    /// click or double-click on the card does the drag too ("click": true). Both ends empty takes the drag out, with
    /// its clicks. While the action is done by clicking a zone instead, the drag can't be filled in.
    VBoxContainer Drag(List<object> path, object? value)
    {
        List<object> actionPath = path[..^1];
        bool clickedZone = ClickKeys.Any(key => NamesZone(Document.GetValue([.. actionPath, key])));
        Dictionary<string, object?>? drag = value as Dictionary<string, object?>;
        VBoxContainer box = new();
        GridContainer grid = new() { Columns = 2 };
        Dictionary<string, LineEdit> ends = [];
        foreach (string end in new[] { "from", "to" })
        {
            grid.AddChild(new Label { Text = end });
            object? current = drag?.GetValueOrDefault(end);
            LineEdit zones = new()
            {
                Text = current is List<object?> names ? string.Join(", ", names.Select(Values.Text)) : AsText(current),
                PlaceholderText = end == "from" ? "any zone" : "e.g. pile, or tableau, foundation",
                SizeFlagsHorizontal = SizeFlags.ExpandFill, Editable = !clickedZone,
            };
            if (clickedZone)
            {
                zones.PlaceholderText = "done by clicking a zone: empty click first";
                zones.TooltipText = "An action is done by a drag or by clicking a zone, not both. To drag instead, take out click (and double_click).";
            }
            grid.AddChild(zones);
            ends[end] = zones;
        }
        box.AddChild(grid);
        void Send()
        {
            Dictionary<string, object?> newDrag = [];
            foreach ((string end, LineEdit zones) in ends)
            {
                List<string> names = SplitNames(zones.Text);
                if (names.Count > 0)
                {
                    newDrag[end] = names.Count == 1 ? names[0] : names.Cast<object?>().ToList();
                }
            }
            if (Values.DeepEquals(newDrag, drag ?? []))
            {
                return;
            }
            if (newDrag.Count > 0)
            {
                Changed?.Invoke(path, newDrag);
                return;
            }
            editor.Edit(() => TakeOutDrag(path));
        }
        foreach (LineEdit zones in ends.Values)
        {
            zones.TextSubmitted += _ => Send();
            zones.FocusExited += Send;
        }
        HBoxContainer clicks = new();
        foreach (string key in ClickKeys)
        {
            CheckBox check = new()
            {
                Text = "also by " + key.Replace("_", "-") + "ing", ButtonPressed = Document.GetValue([.. actionPath, key]) is true, Disabled = drag == null,
                TooltipText = drag != null ? "Clicking the card does the drag: it goes to the first zone the drag allows." : "Fill in the drag first.",
            };
            check.Toggled += on => Changed?.Invoke([.. actionPath, key], on ? true : null);
            clicks.AddChild(check);
        }
        box.AddChild(clicks);
        return box;
    }

    /// Without the drag, "click": true would mean nothing, so it goes too.
    void TakeOutDrag(List<object> path)
    {
        Document.Remove(path);
        foreach (string key in ClickKeys)
        {
            if (Document.GetValue([.. path[..^1], key]) is bool)
            {
                Document.Remove([.. path[..^1], key]);
            }
        }
    }

    /// For an action without a drag: the zone (or zones) whose cards are clicked. A drag action is clicked with the
    /// drag's own check boxes, so then this stays empty.
    LineEdit Click(List<object> path, object? value)
    {
        LineEdit zones = new()
        {
            Text = value is List<object?> names ? string.Join(", ", names.Select(Values.Text)) : value as string ?? "", PlaceholderText = "e.g. deck",
        };
        if (Document.Has([.. path[..^1], "drag"]))
        {
            zones.Editable = false;
            zones.Text = "";
            zones.PlaceholderText = "done by its drag";
            zones.TooltipText = "This action has a drag. To let a click do it, tick \"also by clicking\" under drag.";
        }
        void Send()
        {
            List<string> names = SplitNames(zones.Text);
            object? newValue = names.Count == 0 ? null : names.Count == 1 ? names[0] : names.Cast<object?>().ToList();
            if (zones.Editable && !Values.DeepEquals(newValue, NamesZone(value) ? value : null))
            {
                Changed?.Invoke(path, newValue);
            }
        }
        zones.TextSubmitted += _ => Send();
        zones.FocusExited += Send;
        return zones;
    }

    static bool NamesZone(object? click) => click is string or List<object?>;

    /// A board's shape, columns, rows and space size, each changed on its own (the first change makes the whole board,
    /// so it's never half made), and its paint as JSONC text once there's a board.
    VBoxContainer Board(List<object> path, object? value)
    {
        Dictionary<string, object?>? board = value as Dictionary<string, object?>;
        void Send(string key, object? newValue)
        {
            if (board != null)
            {
                Changed?.Invoke([.. path, key], newValue);
                return;
            }
            Dictionary<string, object?> made = new() { ["shape"] = Core.Board.Square, ["columns"] = 8L, ["rows"] = 8L, ["size"] = Core.Board.DefaultSpaceSize };
            made[key] = newValue;
            Changed?.Invoke(path, made);
        }
        GridContainer grid = new() { Columns = 2 };
        OptionButton shape = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (string option in Core.Board.Shapes)
        {
            shape.AddItem(option);
        }
        shape.Select(Array.IndexOf(Core.Board.Shapes, board?.GetValueOrDefault("shape") as string ?? Core.Board.Square));
        shape.ItemSelected += index => Send("shape", Core.Board.Shapes[index]);
        grid.AddChild(new Label { Text = "shape" });
        grid.AddChild(shape);
        foreach ((string key, double fallback, double least) in new[] { ("columns", 8.0, 1.0), ("rows", 8.0, 1.0), ("size", Core.Board.DefaultSpaceSize, 10.0) })
        {
            SpinBox spin = new()
            {
                MinValue = least, AllowGreater = true, Step = 1, Suffix = key == "size" ? "px" : "", SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Value = Values.IsNumber(board?.GetValueOrDefault(key)) ? Values.ToDouble(board![key]) : fallback,
            };
            spin.ValueChanged += number => Send(key, key == "size" ? number : (long)number);
            grid.AddChild(new Label { Text = key });
            grid.AddChild(spin);
        }
        VBoxContainer box = new();
        box.AddChild(grid);
        box.AddChild(new Label { Text = "paint (conditions with their colors)" });
        TextEdit paint = JsonBox([.. path, "paint"], board?.GetValueOrDefault("paint"), "{ \"top_edge\": \"#c8443a\" }");
        paint.Editable = board != null;
        box.AddChild(paint);
        return box;
    }

    /// Anything else, as JSONC text; only sent when it reads correctly.
    TextEdit JsonBox(List<object> path, object? value, string example)
    {
        TextEdit box = new()
        {
            Text = value != null ? GameDocument.OneLine(value) : "", ScrollFitContentHeight = true, WrapMode = TextEdit.LineWrappingMode.Boundary,
            PlaceholderText = "e.g. " + example,
        };
        box.AddThemeFontOverride("font", EditorTheme.CodeFont);
        box.FocusExited += () =>
        {
            if (box.Text.Trim().Length == 0)
            {
                Changed?.Invoke(path, null);
                return;
            }
            object? parsed = ParseOrNull(box.Text);
            box.AddThemeColorOverride("font_color", parsed == null ? EditorTheme.Error : EditorTheme.Text);
            if (parsed != null)
            {
                Changed?.Invoke(path, parsed);
            }
        };
        return box;
    }
}

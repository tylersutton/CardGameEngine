using Godot;

/// The editor's look: a compact dark theme in the style of VS Code. Colors are named here so the rest of the editor
/// can use them too. Variations (ThemeTypeVariation) name the editor's own kinds of panel, label and button.
public static class EditorTheme
{
    public static readonly Color Background = new("1f1f1f");
    public static readonly Color Panel = new("181818");
    public static readonly Color InputBackground = new("2a2a2a");
    public static readonly Color Border = new("2b2b2b");
    public static readonly Color InputBorder = new("3c3c3c");
    public static readonly Color Hover = new(1, 1, 1, 0.07f);
    public static readonly Color Pressed = new(1, 1, 1, 0.12f);
    public static readonly Color Selected = new("04395e");
    public static readonly Color Accent = new("0078d4");
    public static readonly Color Text = new("cccccc");
    public static readonly Color TextDim = new("8b8b8b");
    public static readonly Color TextBright = new("ffffff");
    public static readonly Color Error = new("f14c4c");
    public static readonly Color Warning = new("cca700");
    public static readonly Color Good = new("89d185");
    public static readonly Color Playing = new("b5541c");
    public static readonly Color StringColor = new("ce9178");
    public static readonly Color NumberColor = new("b5cea8");
    public static readonly Color Keyword = new("569cd6");
    public static readonly Color Comment = new("6a9955");
    public static readonly Color Function = new("dcdcaa");
    const int FontSize = 13;
    const int SmallFontSize = 11;

    static readonly SystemFont uiFont = SystemFontNamed(["Segoe UI", "Inter", "Helvetica Neue", "Arial", "sans-serif"], 400);
    static readonly SystemFont boldFont = SystemFontNamed(["Segoe UI Semibold", "Segoe UI", "Inter", "Helvetica Neue", "Arial", "sans-serif"], 600);
    public static readonly SystemFont CodeFont = SystemFontNamed(["Cascadia Mono", "Consolas", "Menlo", "Courier New", "monospace"], 400);

    public static Theme Build()
    {
        Theme theme = new() { DefaultFont = uiFont, DefaultFontSize = FontSize };
        Containers(theme);
        Buttons(theme);
        TextBoxes(theme);
        Lists(theme);
        Popups(theme);
        Variations(theme);
        return theme;
    }

    static SystemFont SystemFontNamed(string[] names, int weight) =>
        new() { FontNames = names, FontWeight = weight, SubpixelPositioning = TextServer.SubpixelPositioning.Disabled };

    /// A flat box: its color, an optional border (on the given sides: left, top, right, bottom), corner radius and
    /// inner margins.
    public static StyleBoxFlat Box(Color color, Color? borderColor = null, int[]? sides = null, int radius = 0, Vector2? margins = null)
    {
        int[] borders = sides ?? [0, 0, 0, 0];
        Vector2 inner = margins ?? new Vector2(6, 3);
        StyleBoxFlat style = new()
        {
            BgColor = color, BorderColor = borderColor ?? Colors.Transparent,
            BorderWidthLeft = borders[0], BorderWidthTop = borders[1], BorderWidthRight = borders[2], BorderWidthBottom = borders[3],
            ContentMarginLeft = inner.X, ContentMarginRight = inner.X, ContentMarginTop = inner.Y, ContentMarginBottom = inner.Y,
            AntiAliasing = false,
        };
        style.SetCornerRadiusAll(radius);
        return style;
    }

    /// The control inside a margin.
    public static MarginContainer Padded(Control child, int horizontal, int vertical)
    {
        MarginContainer margin = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_left", horizontal);
        margin.AddThemeConstantOverride("margin_right", horizontal);
        margin.AddThemeConstantOverride("margin_top", vertical);
        margin.AddThemeConstantOverride("margin_bottom", vertical);
        margin.AddChild(child);
        return margin;
    }

    static StyleBoxEmpty Empty(Vector2? margins = null)
    {
        Vector2 inner = margins ?? new Vector2(6, 3);
        return new() { ContentMarginLeft = inner.X, ContentMarginRight = inner.X, ContentMarginTop = inner.Y, ContentMarginBottom = inner.Y };
    }

    static void Containers(Theme theme)
    {
        theme.SetStylebox("panel", "Panel", Box(Background));
        theme.SetStylebox("panel", "PanelContainer", Box(Background, margins: Vector2.Zero));
        foreach (string split in new[] { "HSplitContainer", "VSplitContainer", "SplitContainer" })
        {
            theme.SetConstant("separation", split, 1);
            theme.SetConstant("minimum_grab_thickness", split, 8);
            theme.SetConstant("autohide", split, 1);
            theme.SetIcon("grabber", split, new ImageTexture());
            theme.SetIcon("h_grabber", split, new ImageTexture());
            theme.SetIcon("v_grabber", split, new ImageTexture());
            theme.SetStylebox("split_bar_background", split, Box(Border, margins: Vector2.Zero));
        }
        // Scroll bars are 12 pixels wide, so they're easy to grab.
        foreach (string scrollBar in new[] { "VScrollBar", "HScrollBar" })
        {
            theme.SetStylebox("scroll", scrollBar, Box(new(1, 1, 1, 0.03f), margins: new Vector2(6, 6)));
            theme.SetStylebox("scroll_focus", scrollBar, Box(new(1, 1, 1, 0.03f), margins: new Vector2(6, 6)));
            theme.SetStylebox("grabber", scrollBar, Box(new(1, 1, 1, 0.22f), radius: 5, margins: new Vector2(6, 6)));
            theme.SetStylebox("grabber_highlight", scrollBar, Box(new(1, 1, 1, 0.32f), radius: 5, margins: new Vector2(6, 6)));
            theme.SetStylebox("grabber_pressed", scrollBar, Box(new(1, 1, 1, 0.42f), radius: 5, margins: new Vector2(6, 6)));
            foreach (string arrow in new[] { "increment", "increment_highlight", "increment_pressed", "decrement", "decrement_highlight", "decrement_pressed" })
            {
                theme.SetIcon(arrow, scrollBar, new ImageTexture());
            }
        }
        theme.SetConstant("separation", "HBoxContainer", 4);
        theme.SetConstant("separation", "VBoxContainer", 4);
        theme.SetConstant("h_separation", "GridContainer", 6);
        theme.SetConstant("v_separation", "GridContainer", 4);
        theme.SetStylebox("separator", "HSeparator", Box(Border, margins: Vector2.Zero));
        theme.SetConstant("separation", "HSeparator", 9);
        theme.SetStylebox("separator", "VSeparator", Box(Border, margins: Vector2.Zero));
        theme.SetConstant("separation", "VSeparator", 9);
        theme.SetColor("font_color", "Label", Text);
    }

    static void Buttons(Theme theme)
    {
        int[] allSides = [1, 1, 1, 1];
        StyleBoxFlat normal = Box(new("313131"), InputBorder, allSides, 3, new Vector2(8, 3));
        StyleBoxFlat hover = Box(new("3a3a3a"), InputBorder, allSides, 3, new Vector2(8, 3));
        StyleBoxFlat pressed = Box(new("2a2a2a"), InputBorder, allSides, 3, new Vector2(8, 3));
        StyleBoxFlat disabled = Box(new("262626"), Border, allSides, 3, new Vector2(8, 3));
        StyleBoxFlat focus = Box(Colors.Transparent, Accent, allSides, 3);
        foreach (string type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            theme.SetStylebox("normal", type, normal);
            theme.SetStylebox("hover", type, hover);
            theme.SetStylebox("pressed", type, pressed);
            theme.SetStylebox("hover_pressed", type, hover);
            theme.SetStylebox("disabled", type, disabled);
            theme.SetStylebox("focus", type, focus);
            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_hover_color", type, TextBright);
            theme.SetColor("font_pressed_color", type, TextBright);
            theme.SetColor("font_hover_pressed_color", type, TextBright);
            theme.SetColor("font_focus_color", type, Text);
            theme.SetColor("font_disabled_color", type, new Color(TextDim, 0.6f));
            theme.SetColor("icon_normal_color", type, Text);
            theme.SetColor("icon_hover_color", type, TextBright);
            theme.SetColor("icon_pressed_color", type, TextBright);
            theme.SetColor("icon_disabled_color", type, new Color(TextDim, 0.5f));
            theme.SetConstant("h_separation", type, 5);
        }
        theme.SetIcon("arrow", "OptionButton", EditorIcons.Icon("chevron_down", Text));
        foreach (string type in new[] { "CheckBox", "CheckButton" })
        {
            foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            {
                theme.SetStylebox(state, type, Empty(new Vector2(2, 2)));
            }
            theme.SetStylebox("focus", type, Empty());
            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_hover_color", type, TextBright);
            theme.SetColor("font_pressed_color", type, Text);
            theme.SetColor("font_hover_pressed_color", type, TextBright);
            theme.SetColor("font_focus_color", type, Text);
        }
        theme.SetIcon("checked", "CheckBox", EditorIcons.CheckBox(true));
        theme.SetIcon("unchecked", "CheckBox", EditorIcons.CheckBox(false));
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            theme.SetStylebox(state, "MenuBar", Empty(new Vector2(8, 4)));
        }
        theme.SetStylebox("hover", "MenuBar", Box(Hover, radius: 3, margins: new Vector2(8, 4)));
        theme.SetStylebox("pressed", "MenuBar", Box(Pressed, radius: 3, margins: new Vector2(8, 4)));
        theme.SetColor("font_color", "MenuBar", Text);
        theme.SetColor("font_hover_color", "MenuBar", TextBright);
        theme.SetColor("font_pressed_color", "MenuBar", TextBright);
        theme.SetConstant("h_separation", "MenuBar", 0);
    }

    static void TextBoxes(Theme theme)
    {
        int[] allSides = [1, 1, 1, 1];
        StyleBoxFlat normal = Box(InputBackground, InputBorder, allSides, 3, new Vector2(6, 3));
        StyleBoxFlat focus = Box(InputBackground, Accent, allSides, 3, new Vector2(6, 3));
        StyleBoxFlat readOnly = Box(new("232323"), Border, allSides, 3, new Vector2(6, 3));
        foreach (string type in new[] { "LineEdit", "TextEdit", "CodeEdit" })
        {
            theme.SetStylebox("normal", type, normal);
            theme.SetStylebox("focus", type, focus);
            theme.SetStylebox("read_only", type, readOnly);
            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_placeholder_color", type, new Color(TextDim, 0.7f));
            theme.SetColor("caret_color", type, TextBright);
            theme.SetColor("selection_color", type, new Color("264f78"));
            theme.SetColor("font_selected_color", type, new Color(0, 0, 0, 0));
        }
        theme.SetColor("background_color", "TextEdit", Colors.Transparent);
        theme.SetColor("current_line_color", "TextEdit", Colors.Transparent);
        theme.SetFont("font", "CodeEdit", CodeFont);
        theme.SetColor("background_color", "CodeEdit", Colors.Transparent);
        theme.SetColor("current_line_color", "CodeEdit", new Color(1, 1, 1, 0.04f));
        theme.SetColor("line_number_color", "CodeEdit", new Color("6e7681"));
        theme.SetColor("completion_background_color", "CodeEdit", new Color("202020"));
        theme.SetColor("completion_selected_color", "CodeEdit", Selected);
        theme.SetColor("completion_existing_color", "CodeEdit", new Color(Accent, 0.3f));
        theme.SetColor("completion_font_color", "CodeEdit", Text);
        theme.SetColor("brace_mismatch_color", "CodeEdit", Error);
        theme.SetColor("code_folding_color", "CodeEdit", TextDim);
        theme.SetColor("folded_code_region_color", "CodeEdit", new Color(Accent, 0.2f));
        theme.SetConstant("completion_lines", "CodeEdit", 10);
    }

    static void Lists(Theme theme)
    {
        StyleBoxFlat rowSelected = Box(Selected, margins: new Vector2(4, 1));
        StyleBoxFlat rowFocused = Box(Selected, Accent, [1, 1, 1, 1], 0, new Vector2(4, 1));
        StyleBoxFlat rowHover = Box(Hover, margins: new Vector2(4, 1));
        StyleBoxFlat unfocused = Box(new(1, 1, 1, 0.09f), margins: new Vector2(4, 1));
        foreach (string type in new[] { "Tree", "ItemList" })
        {
            theme.SetStylebox("panel", type, Box(Colors.Transparent, margins: new Vector2(0, 2)));
            theme.SetStylebox("focus", type, Empty());
            theme.SetStylebox("selected", type, unfocused);
            theme.SetStylebox("selected_focus", type, rowSelected);
            theme.SetStylebox("hovered", type, rowHover);
            theme.SetStylebox("hovered_dimmed", type, rowHover);
            theme.SetStylebox("cursor", type, rowFocused);
            theme.SetStylebox("cursor_unfocused", type, Empty());
            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_selected_color", type, TextBright);
            theme.SetColor("font_hovered_color", type, TextBright);
            theme.SetColor("guide_color", type, Colors.Transparent);
        }
        theme.SetConstant("v_separation", "Tree", 2);
        theme.SetConstant("h_separation", "Tree", 4);
        theme.SetConstant("item_margin", "Tree", 12);
        theme.SetConstant("inner_item_margin_left", "Tree", 2);
        theme.SetConstant("inner_item_margin_right", "Tree", 8);
        theme.SetConstant("button_margin", "Tree", 2);
        theme.SetConstant("draw_guides", "Tree", 0);
        theme.SetConstant("draw_relationship_lines", "Tree", 0);
        theme.SetConstant("scroll_border", "Tree", 4);
        theme.SetStylebox("button_pressed", "Tree", Box(Pressed, radius: 3, margins: new Vector2(1, 1)));
        theme.SetStylebox("button_hover", "Tree", Box(Hover, radius: 3, margins: new Vector2(1, 1)));
        theme.SetStylebox("title_button_normal", "Tree", Box(Panel, Border, [0, 0, 1, 1], 0, new Vector2(6, 3)));
        theme.SetStylebox("title_button_hover", "Tree", Box(new("222222"), Border, [0, 0, 1, 1], 0, new Vector2(6, 3)));
        theme.SetStylebox("title_button_pressed", "Tree", Box(new("222222"), Border, [0, 0, 1, 1], 0, new Vector2(6, 3)));
        theme.SetColor("title_button_color", "Tree", TextDim);
        theme.SetFont("title_button_font", "Tree", boldFont);
        theme.SetIcon("arrow", "Tree", EditorIcons.Icon("chevron_down", TextDim, 0.75f));
        theme.SetIcon("arrow_collapsed", "Tree", EditorIcons.Icon("chevron_right", TextDim, 0.75f));
        theme.SetConstant("v_separation", "ItemList", 2);
        theme.SetConstant("h_separation", "ItemList", 6);
        theme.SetConstant("line_separation", "ItemList", 2);
        foreach (string state in new[] { "tab_selected", "tab_unselected", "tab_hovered", "tab_disabled", "tab_focus" })
        {
            theme.SetStylebox(state, "TabBar", Empty(new Vector2(12, 6)));
        }
        theme.SetStylebox("tab_selected", "TabBar", Box(Background, Accent, [0, 1, 0, 0], 0, new Vector2(12, 6)));
        theme.SetStylebox("tab_hovered", "TabBar", Box(new("1c1c1c"), margins: new Vector2(12, 6)));
        theme.SetColor("font_selected_color", "TabBar", TextBright);
        theme.SetColor("font_unselected_color", "TabBar", TextDim);
        theme.SetColor("font_hovered_color", "TabBar", Text);
        theme.SetConstant("h_separation", "TabBar", 6);
    }

    static void Popups(Theme theme)
    {
        StyleBoxFlat popup = Box(new("1f1f1f"), new Color("454545"), [1, 1, 1, 1], 5, new Vector2(4, 4));
        popup.ShadowColor = new(0, 0, 0, 0.45f);
        popup.ShadowSize = 8;
        theme.SetStylebox("panel", "PopupMenu", popup);
        theme.SetStylebox("panel", "PopupPanel", popup);
        theme.SetStylebox("hover", "PopupMenu", Box(Selected, radius: 3));
        theme.SetStylebox("separator", "PopupMenu", Box(new("454545"), margins: Vector2.Zero));
        theme.SetColor("font_color", "PopupMenu", Text);
        theme.SetColor("font_hover_color", "PopupMenu", TextBright);
        theme.SetColor("font_accelerator_color", "PopupMenu", TextDim);
        theme.SetColor("font_disabled_color", "PopupMenu", new Color(TextDim, 0.6f));
        theme.SetColor("font_separator_color", "PopupMenu", TextDim);
        theme.SetConstant("v_separation", "PopupMenu", 6);
        theme.SetConstant("item_start_padding", "PopupMenu", 10);
        theme.SetConstant("item_end_padding", "PopupMenu", 14);
        theme.SetIcon("submenu", "PopupMenu", EditorIcons.Icon("chevron_right", TextDim));
        theme.SetIcon("checked", "PopupMenu", EditorIcons.Icon("check", Text));
        theme.SetStylebox("panel", "TooltipPanel", Box(new("252526"), new Color("454545"), [1, 1, 1, 1], 3, new Vector2(8, 5)));
        theme.SetColor("font_color", "TooltipLabel", Text);
        theme.SetFontSize("font_size", "TooltipLabel", 12);
        StyleBoxFlat window = Box(new("1f1f1f"), new Color("454545"), [1, 1, 1, 1], 6, Vector2.Zero);
        window.ExpandMarginTop = 28;
        window.ExpandMarginLeft = 1;
        window.ExpandMarginRight = 1;
        window.ExpandMarginBottom = 1;
        window.ShadowColor = new(0, 0, 0, 0.5f);
        window.ShadowSize = 12;
        theme.SetStylebox("embedded_border", "Window", window);
        theme.SetStylebox("embedded_unfocused_border", "Window", window);
        theme.SetColor("title_color", "Window", Text);
        theme.SetFont("title_font", "Window", boldFont);
        theme.SetFontSize("title_font_size", "Window", FontSize);
        theme.SetConstant("title_height", "Window", 28);
        theme.SetIcon("close", "Window", EditorIcons.Icon("close", TextDim));
        theme.SetIcon("close_pressed", "Window", EditorIcons.Icon("close", TextBright));
        theme.SetStylebox("panel", "AcceptDialog", Box(new("1f1f1f"), radius: 6, margins: new Vector2(12, 10)));
    }

    /// The editor's own kinds of panel, label and button, used through ThemeTypeVariation.
    static void Variations(Theme theme)
    {
        theme.SetTypeVariation("Bar", "PanelContainer");
        theme.SetStylebox("panel", "Bar", Box(Panel, Border, [0, 0, 0, 1], 0, new Vector2(4, 0)));
        theme.SetTypeVariation("SideBar", "PanelContainer");
        theme.SetStylebox("panel", "SideBar", Box(Panel, margins: Vector2.Zero));
        theme.SetTypeVariation("ActivityBar", "PanelContainer");
        theme.SetStylebox("panel", "ActivityBar", Box(Panel, Border, [0, 0, 1, 0], 0, new Vector2(0, 4)));
        theme.SetTypeVariation("StatusBar", "PanelContainer");
        theme.SetStylebox("panel", "StatusBar", Box(Panel, Border, [0, 1, 0, 0], 0, new Vector2(6, 0)));
        theme.SetTypeVariation("Card", "PanelContainer");
        theme.SetStylebox("panel", "Card", Box(new("252526"), Border, [1, 1, 1, 1], 4, new Vector2(8, 6)));
        theme.SetTypeVariation("HeaderLabel", "Label");
        theme.SetFont("font", "HeaderLabel", boldFont);
        theme.SetFontSize("font_size", "HeaderLabel", SmallFontSize);
        theme.SetColor("font_color", "HeaderLabel", TextDim);
        theme.SetTypeVariation("TitleLabel", "Label");
        theme.SetFont("font", "TitleLabel", boldFont);
        theme.SetColor("font_color", "TitleLabel", TextBright);
        theme.SetTypeVariation("DimLabel", "Label");
        theme.SetColor("font_color", "DimLabel", TextDim);
        theme.SetFontSize("font_size", "DimLabel", 12);
        theme.SetTypeVariation("FlatButton", "Button");
        foreach (string state in new[] { "normal", "disabled", "focus" })
        {
            theme.SetStylebox(state, "FlatButton", Empty(new Vector2(4, 2)));
        }
        theme.SetStylebox("hover", "FlatButton", Box(Hover, radius: 4, margins: new Vector2(4, 2)));
        theme.SetStylebox("pressed", "FlatButton", Box(Pressed, radius: 4, margins: new Vector2(4, 2)));
        theme.SetStylebox("hover_pressed", "FlatButton", Box(Pressed, radius: 4, margins: new Vector2(4, 2)));
        theme.SetColor("font_color", "FlatButton", Text);
        theme.SetTypeVariation("ActivityButton", "Button");
        foreach (string state in new[] { "normal", "hover", "disabled", "focus" })
        {
            theme.SetStylebox(state, "ActivityButton", Empty(new Vector2(12, 10)));
        }
        theme.SetStylebox("pressed", "ActivityButton", Box(Colors.Transparent, TextBright, [2, 0, 0, 0], 0, new Vector2(12, 10)));
        theme.SetStylebox("hover_pressed", "ActivityButton", Box(Colors.Transparent, TextBright, [2, 0, 0, 0], 0, new Vector2(12, 10)));
        theme.SetColor("icon_normal_color", "ActivityButton", new Color("868686"));
        theme.SetColor("icon_hover_color", "ActivityButton", TextBright);
        theme.SetColor("icon_pressed_color", "ActivityButton", TextBright);
        theme.SetColor("icon_hover_pressed_color", "ActivityButton", TextBright);
        theme.SetTypeVariation("AccentButton", "Button");
        theme.SetStylebox("normal", "AccentButton", Box(Accent, radius: 3, margins: new Vector2(10, 3)));
        theme.SetStylebox("hover", "AccentButton", Box(new("1c8ae0"), radius: 3, margins: new Vector2(10, 3)));
        theme.SetStylebox("pressed", "AccentButton", Box(new("0068b8"), radius: 3, margins: new Vector2(10, 3)));
        theme.SetColor("font_color", "AccentButton", TextBright);
        theme.SetColor("icon_normal_color", "AccentButton", TextBright);
        theme.SetTypeVariation("GroupButton", "Button");
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
        {
            theme.SetStylebox(state, "GroupButton", Box(Colors.Transparent, Border, [0, 0, 0, 1], 0, new Vector2(2, 4)));
        }
        theme.SetFont("font", "GroupButton", boldFont);
        theme.SetFontSize("font_size", "GroupButton", SmallFontSize);
        theme.SetColor("font_color", "GroupButton", TextDim);
        theme.SetColor("font_hover_color", "GroupButton", Text);
        theme.SetColor("font_pressed_color", "GroupButton", TextDim);
        theme.SetColor("font_hover_pressed_color", "GroupButton", Text);
        theme.SetColor("icon_normal_color", "GroupButton", TextDim);
        theme.SetConstant("h_separation", "GroupButton", 4);
        theme.SetTypeVariation("StatusButton", "Button");
        foreach (string state in new[] { "normal", "focus", "disabled" })
        {
            theme.SetStylebox(state, "StatusButton", Empty(new Vector2(6, 2)));
        }
        theme.SetStylebox("hover", "StatusButton", Box(Hover, margins: new Vector2(6, 2)));
        theme.SetStylebox("pressed", "StatusButton", Box(Pressed, margins: new Vector2(6, 2)));
        theme.SetFontSize("font_size", "StatusButton", 12);
        theme.SetColor("font_color", "StatusButton", Text);
    }
}

class_name EditorTheme
## The editor's look: a compact dark theme in the style of VS Code. Colors are named here so the rest of the
## editor can use them too. Variations (theme_type_variation) name the editor's own kinds of panel, label and button.

const BACKGROUND := Color("1f1f1f")
const PANEL := Color("181818")
const INPUT := Color("2a2a2a")
const BORDER := Color("2b2b2b")
const INPUT_BORDER := Color("3c3c3c")
const HOVER := Color(1, 1, 1, 0.07)
const PRESSED := Color(1, 1, 1, 0.12)
const SELECTED := Color("04395e")
const ACCENT := Color("0078d4")
const TEXT := Color("cccccc")
const TEXT_DIM := Color("8b8b8b")
const TEXT_BRIGHT := Color("ffffff")
const ERROR := Color("f14c4c")
const WARNING := Color("cca700")
const GOOD := Color("89d185")
const PLAYING := Color("b5541c")
const STRING := Color("ce9178")
const NUMBER := Color("b5cea8")
const KEYWORD := Color("569cd6")
const COMMENT := Color("6a9955")
const FUNCTION := Color("dcdcaa")
const FONT_SIZE := 13
const SMALL_FONT_SIZE := 11

static var ui_font := _system_font(["Segoe UI", "Inter", "Helvetica Neue", "Arial", "sans-serif"], 400)
static var bold_font := _system_font(["Segoe UI Semibold", "Segoe UI", "Inter", "Helvetica Neue", "Arial", "sans-serif"], 600)
static var code_font := _system_font(["Cascadia Mono", "Consolas", "Menlo", "Courier New", "monospace"], 400)


static func build() -> Theme:
	var theme := Theme.new()
	theme.default_font = ui_font
	theme.default_font_size = FONT_SIZE
	_containers(theme)
	_buttons(theme)
	_text_boxes(theme)
	_lists(theme)
	_popups(theme)
	_variations(theme)
	return theme


static func _system_font(names: PackedStringArray, weight: int) -> SystemFont:
	var font := SystemFont.new()
	font.font_names = names
	font.font_weight = weight
	font.subpixel_positioning = TextServer.SUBPIXEL_POSITIONING_DISABLED
	return font


## A flat box: its color, an optional border (on the given sides: left, top, right, bottom), corner radius and
## inner margins.
static func box(color: Color, border_color := Color.TRANSPARENT, sides := [0, 0, 0, 0], radius := 0, margins := Vector2(6, 3)) -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = color
	style.border_color = border_color
	style.border_width_left = sides[0]
	style.border_width_top = sides[1]
	style.border_width_right = sides[2]
	style.border_width_bottom = sides[3]
	style.set_corner_radius_all(radius)
	style.content_margin_left = margins.x
	style.content_margin_right = margins.x
	style.content_margin_top = margins.y
	style.content_margin_bottom = margins.y
	style.anti_aliasing = false
	return style


## The control inside a margin.
static func padded(child: Control, horizontal: int, vertical: int) -> MarginContainer:
	var margin := MarginContainer.new()
	margin.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for side in ["left", "right"]:
		margin.add_theme_constant_override("margin_" + side, horizontal)
	for side in ["top", "bottom"]:
		margin.add_theme_constant_override("margin_" + side, vertical)
	margin.add_child(child)
	return margin


static func _empty(margins := Vector2(6, 3)) -> StyleBoxEmpty:
	var style := StyleBoxEmpty.new()
	style.content_margin_left = margins.x
	style.content_margin_right = margins.x
	style.content_margin_top = margins.y
	style.content_margin_bottom = margins.y
	return style


static func _containers(theme: Theme) -> void:
	theme.set_stylebox("panel", "Panel", box(BACKGROUND))
	theme.set_stylebox("panel", "PanelContainer", box(BACKGROUND, Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2.ZERO))
	for split in ["HSplitContainer", "VSplitContainer", "SplitContainer"]:
		theme.set_constant("separation", split, 1)
		theme.set_constant("minimum_grab_thickness", split, 8)
		theme.set_constant("autohide", split, 1)
		theme.set_icon("grabber", split, ImageTexture.new())
		theme.set_icon("h_grabber", split, ImageTexture.new())
		theme.set_icon("v_grabber", split, ImageTexture.new())
		theme.set_stylebox("split_bar_background", split, box(BORDER, Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2.ZERO))
	# Scroll bars are 12 pixels wide, so they're easy to grab.
	for scroll_bar in ["VScrollBar", "HScrollBar"]:
		theme.set_stylebox("scroll", scroll_bar, box(Color(1, 1, 1, 0.03), Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2(6, 6)))
		theme.set_stylebox("scroll_focus", scroll_bar, box(Color(1, 1, 1, 0.03), Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2(6, 6)))
		theme.set_stylebox("grabber", scroll_bar, box(Color(1, 1, 1, 0.22), Color.TRANSPARENT, [0, 0, 0, 0], 5, Vector2(6, 6)))
		theme.set_stylebox("grabber_highlight", scroll_bar, box(Color(1, 1, 1, 0.32), Color.TRANSPARENT, [0, 0, 0, 0], 5, Vector2(6, 6)))
		theme.set_stylebox("grabber_pressed", scroll_bar, box(Color(1, 1, 1, 0.42), Color.TRANSPARENT, [0, 0, 0, 0], 5, Vector2(6, 6)))
		for arrow in ["increment", "increment_highlight", "increment_pressed", "decrement", "decrement_highlight", "decrement_pressed"]:
			theme.set_icon(arrow, scroll_bar, ImageTexture.new())
	theme.set_constant("separation", "HBoxContainer", 4)
	theme.set_constant("separation", "VBoxContainer", 4)
	theme.set_constant("h_separation", "GridContainer", 6)
	theme.set_constant("v_separation", "GridContainer", 4)
	theme.set_stylebox("separator", "HSeparator", box(BORDER, Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2.ZERO))
	theme.set_constant("separation", "HSeparator", 9)
	theme.set_stylebox("separator", "VSeparator", box(BORDER, Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2.ZERO))
	theme.set_constant("separation", "VSeparator", 9)
	theme.set_color("font_color", "Label", TEXT)


static func _buttons(theme: Theme) -> void:
	var normal := box(Color("313131"), INPUT_BORDER, [1, 1, 1, 1], 3, Vector2(8, 3))
	var hover := box(Color("3a3a3a"), INPUT_BORDER, [1, 1, 1, 1], 3, Vector2(8, 3))
	var pressed := box(Color("2a2a2a"), INPUT_BORDER, [1, 1, 1, 1], 3, Vector2(8, 3))
	var disabled := box(Color("262626"), BORDER, [1, 1, 1, 1], 3, Vector2(8, 3))
	var focus := box(Color.TRANSPARENT, ACCENT, [1, 1, 1, 1], 3)
	for type in ["Button", "OptionButton", "MenuButton"]:
		theme.set_stylebox("normal", type, normal)
		theme.set_stylebox("hover", type, hover)
		theme.set_stylebox("pressed", type, pressed)
		theme.set_stylebox("hover_pressed", type, hover)
		theme.set_stylebox("disabled", type, disabled)
		theme.set_stylebox("focus", type, focus)
		theme.set_color("font_color", type, TEXT)
		theme.set_color("font_hover_color", type, TEXT_BRIGHT)
		theme.set_color("font_pressed_color", type, TEXT_BRIGHT)
		theme.set_color("font_hover_pressed_color", type, TEXT_BRIGHT)
		theme.set_color("font_focus_color", type, TEXT)
		theme.set_color("font_disabled_color", type, Color(TEXT_DIM, 0.6))
		theme.set_color("icon_normal_color", type, TEXT)
		theme.set_color("icon_hover_color", type, TEXT_BRIGHT)
		theme.set_color("icon_pressed_color", type, TEXT_BRIGHT)
		theme.set_color("icon_disabled_color", type, Color(TEXT_DIM, 0.5))
		theme.set_constant("h_separation", type, 5)
	theme.set_icon("arrow", "OptionButton", EditorIcons.icon("chevron_down", TEXT))
	for type in ["CheckBox", "CheckButton"]:
		for state in ["normal", "hover", "pressed", "hover_pressed", "disabled"]:
			theme.set_stylebox(state, type, _empty(Vector2(2, 2)))
		theme.set_stylebox("focus", type, _empty())
		theme.set_color("font_color", type, TEXT)
		theme.set_color("font_hover_color", type, TEXT_BRIGHT)
		theme.set_color("font_pressed_color", type, TEXT)
		theme.set_color("font_hover_pressed_color", type, TEXT_BRIGHT)
		theme.set_color("font_focus_color", type, TEXT)
	theme.set_icon("checked", "CheckBox", EditorIcons.check_box(true))
	theme.set_icon("unchecked", "CheckBox", EditorIcons.check_box(false))
	for state in ["normal", "hover", "pressed", "disabled"]:
		theme.set_stylebox(state, "MenuBar", _empty(Vector2(8, 4)))
	theme.set_stylebox("hover", "MenuBar", box(HOVER, Color.TRANSPARENT, [0, 0, 0, 0], 3, Vector2(8, 4)))
	theme.set_stylebox("pressed", "MenuBar", box(PRESSED, Color.TRANSPARENT, [0, 0, 0, 0], 3, Vector2(8, 4)))
	theme.set_color("font_color", "MenuBar", TEXT)
	theme.set_color("font_hover_color", "MenuBar", TEXT_BRIGHT)
	theme.set_color("font_pressed_color", "MenuBar", TEXT_BRIGHT)
	theme.set_constant("h_separation", "MenuBar", 0)


static func _text_boxes(theme: Theme) -> void:
	var normal := box(INPUT, INPUT_BORDER, [1, 1, 1, 1], 3, Vector2(6, 3))
	var focus := box(INPUT, ACCENT, [1, 1, 1, 1], 3, Vector2(6, 3))
	var read_only := box(Color("232323"), BORDER, [1, 1, 1, 1], 3, Vector2(6, 3))
	for type in ["LineEdit", "TextEdit", "CodeEdit"]:
		theme.set_stylebox("normal", type, normal)
		theme.set_stylebox("focus", type, focus)
		theme.set_stylebox("read_only", type, read_only)
		theme.set_color("font_color", type, TEXT)
		theme.set_color("font_placeholder_color", type, Color(TEXT_DIM, 0.7))
		theme.set_color("caret_color", type, TEXT_BRIGHT)
		theme.set_color("selection_color", type, Color("264f78"))
		theme.set_color("font_selected_color", type, Color(0, 0, 0, 0))
	theme.set_color("background_color", "TextEdit", Color.TRANSPARENT)
	theme.set_color("current_line_color", "TextEdit", Color.TRANSPARENT)
	theme.set_font("font", "CodeEdit", code_font)
	theme.set_color("background_color", "CodeEdit", Color.TRANSPARENT)
	theme.set_color("current_line_color", "CodeEdit", Color(1, 1, 1, 0.04))
	theme.set_color("line_number_color", "CodeEdit", Color("6e7681"))
	theme.set_color("completion_background_color", "CodeEdit", Color("202020"))
	theme.set_color("completion_selected_color", "CodeEdit", SELECTED)
	theme.set_color("completion_existing_color", "CodeEdit", Color(ACCENT, 0.3))
	theme.set_color("completion_font_color", "CodeEdit", TEXT)
	theme.set_color("brace_mismatch_color", "CodeEdit", ERROR)
	theme.set_color("code_folding_color", "CodeEdit", TEXT_DIM)
	theme.set_color("folded_code_region_color", "CodeEdit", Color(ACCENT, 0.2))
	theme.set_constant("completion_lines", "CodeEdit", 10)


static func _lists(theme: Theme) -> void:
	var row_selected := box(SELECTED, Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2(4, 1))
	var row_focused := box(SELECTED, ACCENT, [1, 1, 1, 1], 0, Vector2(4, 1))
	var row_hover := box(HOVER, Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2(4, 1))
	var unfocused := box(Color(1, 1, 1, 0.09), Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2(4, 1))
	for type in ["Tree", "ItemList"]:
		theme.set_stylebox("panel", type, box(Color.TRANSPARENT, Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2(0, 2)))
		theme.set_stylebox("focus", type, _empty())
		theme.set_stylebox("selected", type, unfocused)
		theme.set_stylebox("selected_focus", type, row_selected)
		theme.set_stylebox("hovered", type, row_hover)
		theme.set_stylebox("hovered_dimmed", type, row_hover)
		theme.set_stylebox("cursor", type, row_focused)
		theme.set_stylebox("cursor_unfocused", type, _empty())
		theme.set_color("font_color", type, TEXT)
		theme.set_color("font_selected_color", type, TEXT_BRIGHT)
		theme.set_color("font_hovered_color", type, TEXT_BRIGHT)
		theme.set_color("guide_color", type, Color.TRANSPARENT)
	theme.set_constant("v_separation", "Tree", 2)
	theme.set_constant("h_separation", "Tree", 4)
	theme.set_constant("item_margin", "Tree", 12)
	theme.set_constant("inner_item_margin_left", "Tree", 2)
	theme.set_constant("inner_item_margin_right", "Tree", 8)
	theme.set_constant("button_margin", "Tree", 2)
	theme.set_constant("draw_guides", "Tree", 0)
	theme.set_constant("draw_relationship_lines", "Tree", 0)
	theme.set_constant("scroll_border", "Tree", 4)
	theme.set_stylebox("button_pressed", "Tree", box(PRESSED, Color.TRANSPARENT, [0, 0, 0, 0], 3, Vector2(1, 1)))
	theme.set_stylebox("button_hover", "Tree", box(HOVER, Color.TRANSPARENT, [0, 0, 0, 0], 3, Vector2(1, 1)))
	theme.set_stylebox("title_button_normal", "Tree", box(PANEL, BORDER, [0, 0, 1, 1], 0, Vector2(6, 3)))
	theme.set_stylebox("title_button_hover", "Tree", box(Color("222222"), BORDER, [0, 0, 1, 1], 0, Vector2(6, 3)))
	theme.set_stylebox("title_button_pressed", "Tree", box(Color("222222"), BORDER, [0, 0, 1, 1], 0, Vector2(6, 3)))
	theme.set_color("title_button_color", "Tree", TEXT_DIM)
	theme.set_font("title_button_font", "Tree", bold_font)
	theme.set_icon("arrow", "Tree", EditorIcons.icon("chevron_down", TEXT_DIM, 0.75))
	theme.set_icon("arrow_collapsed", "Tree", EditorIcons.icon("chevron_right", TEXT_DIM, 0.75))
	theme.set_constant("v_separation", "ItemList", 2)
	theme.set_constant("h_separation", "ItemList", 6)
	theme.set_constant("line_separation", "ItemList", 2)
	for state in ["tab_selected", "tab_unselected", "tab_hovered", "tab_disabled", "tab_focus"]:
		theme.set_stylebox(state, "TabBar", _empty(Vector2(12, 6)))
	theme.set_stylebox("tab_selected", "TabBar", box(BACKGROUND, ACCENT, [0, 1, 0, 0], 0, Vector2(12, 6)))
	theme.set_stylebox("tab_hovered", "TabBar", box(Color("1c1c1c"), Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2(12, 6)))
	theme.set_color("font_selected_color", "TabBar", TEXT_BRIGHT)
	theme.set_color("font_unselected_color", "TabBar", TEXT_DIM)
	theme.set_color("font_hovered_color", "TabBar", TEXT)
	theme.set_constant("h_separation", "TabBar", 6)


static func _popups(theme: Theme) -> void:
	var popup := box(Color("1f1f1f"), Color("454545"), [1, 1, 1, 1], 5, Vector2(4, 4))
	popup.shadow_color = Color(0, 0, 0, 0.45)
	popup.shadow_size = 8
	theme.set_stylebox("panel", "PopupMenu", popup)
	theme.set_stylebox("panel", "PopupPanel", popup)
	theme.set_stylebox("hover", "PopupMenu", box(SELECTED, Color.TRANSPARENT, [0, 0, 0, 0], 3))
	theme.set_stylebox("separator", "PopupMenu", box(Color("454545"), Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2(0, 0)))
	theme.set_color("font_color", "PopupMenu", TEXT)
	theme.set_color("font_hover_color", "PopupMenu", TEXT_BRIGHT)
	theme.set_color("font_accelerator_color", "PopupMenu", TEXT_DIM)
	theme.set_color("font_disabled_color", "PopupMenu", Color(TEXT_DIM, 0.6))
	theme.set_color("font_separator_color", "PopupMenu", TEXT_DIM)
	theme.set_constant("v_separation", "PopupMenu", 6)
	theme.set_constant("item_start_padding", "PopupMenu", 10)
	theme.set_constant("item_end_padding", "PopupMenu", 14)
	theme.set_icon("submenu", "PopupMenu", EditorIcons.icon("chevron_right", TEXT_DIM))
	theme.set_icon("checked", "PopupMenu", EditorIcons.icon("check", TEXT))
	theme.set_stylebox("panel", "TooltipPanel", box(Color("252526"), Color("454545"), [1, 1, 1, 1], 3, Vector2(8, 5)))
	theme.set_color("font_color", "TooltipLabel", TEXT)
	theme.set_font_size("font_size", "TooltipLabel", 12)
	var window := box(Color("1f1f1f"), Color("454545"), [1, 1, 1, 1], 6, Vector2(0, 0))
	window.expand_margin_top = 28
	window.expand_margin_left = 1
	window.expand_margin_right = 1
	window.expand_margin_bottom = 1
	window.shadow_color = Color(0, 0, 0, 0.5)
	window.shadow_size = 12
	theme.set_stylebox("embedded_border", "Window", window)
	theme.set_stylebox("embedded_unfocused_border", "Window", window)
	theme.set_color("title_color", "Window", TEXT)
	theme.set_font("title_font", "Window", bold_font)
	theme.set_font_size("title_font_size", "Window", FONT_SIZE)
	theme.set_constant("title_height", "Window", 28)
	theme.set_icon("close", "Window", EditorIcons.icon("close", TEXT_DIM))
	theme.set_icon("close_pressed", "Window", EditorIcons.icon("close", TEXT_BRIGHT))
	theme.set_stylebox("panel", "AcceptDialog", box(Color("1f1f1f"), Color.TRANSPARENT, [0, 0, 0, 0], 6, Vector2(12, 10)))


## The editor's own kinds of panel, label and button, used through theme_type_variation.
static func _variations(theme: Theme) -> void:
	_variation(theme, "Bar", "PanelContainer")
	theme.set_stylebox("panel", "Bar", box(PANEL, BORDER, [0, 0, 0, 1], 0, Vector2(4, 0)))
	_variation(theme, "SideBar", "PanelContainer")
	theme.set_stylebox("panel", "SideBar", box(PANEL, Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2.ZERO))
	_variation(theme, "ActivityBar", "PanelContainer")
	theme.set_stylebox("panel", "ActivityBar", box(PANEL, BORDER, [0, 0, 1, 0], 0, Vector2(0, 4)))
	_variation(theme, "StatusBar", "PanelContainer")
	theme.set_stylebox("panel", "StatusBar", box(PANEL, BORDER, [0, 1, 0, 0], 0, Vector2(6, 0)))
	_variation(theme, "Card", "PanelContainer")
	theme.set_stylebox("panel", "Card", box(Color("252526"), BORDER, [1, 1, 1, 1], 4, Vector2(8, 6)))
	_variation(theme, "HeaderLabel", "Label")
	theme.set_font("font", "HeaderLabel", bold_font)
	theme.set_font_size("font_size", "HeaderLabel", SMALL_FONT_SIZE)
	theme.set_color("font_color", "HeaderLabel", TEXT_DIM)
	_variation(theme, "TitleLabel", "Label")
	theme.set_font("font", "TitleLabel", bold_font)
	theme.set_color("font_color", "TitleLabel", TEXT_BRIGHT)
	_variation(theme, "DimLabel", "Label")
	theme.set_color("font_color", "DimLabel", TEXT_DIM)
	theme.set_font_size("font_size", "DimLabel", 12)
	_variation(theme, "FlatButton", "Button")
	for state in ["normal", "disabled", "focus"]:
		theme.set_stylebox(state, "FlatButton", _empty(Vector2(4, 2)))
	theme.set_stylebox("hover", "FlatButton", box(HOVER, Color.TRANSPARENT, [0, 0, 0, 0], 4, Vector2(4, 2)))
	theme.set_stylebox("pressed", "FlatButton", box(PRESSED, Color.TRANSPARENT, [0, 0, 0, 0], 4, Vector2(4, 2)))
	theme.set_stylebox("hover_pressed", "FlatButton", box(PRESSED, Color.TRANSPARENT, [0, 0, 0, 0], 4, Vector2(4, 2)))
	theme.set_color("font_color", "FlatButton", TEXT)
	_variation(theme, "ActivityButton", "Button")
	for state in ["normal", "hover", "disabled", "focus"]:
		theme.set_stylebox(state, "ActivityButton", _empty(Vector2(12, 10)))
	theme.set_stylebox("pressed", "ActivityButton", box(Color.TRANSPARENT, TEXT_BRIGHT, [2, 0, 0, 0], 0, Vector2(12, 10)))
	theme.set_stylebox("hover_pressed", "ActivityButton", box(Color.TRANSPARENT, TEXT_BRIGHT, [2, 0, 0, 0], 0, Vector2(12, 10)))
	theme.set_color("icon_normal_color", "ActivityButton", Color("868686"))
	theme.set_color("icon_hover_color", "ActivityButton", TEXT_BRIGHT)
	theme.set_color("icon_pressed_color", "ActivityButton", TEXT_BRIGHT)
	theme.set_color("icon_hover_pressed_color", "ActivityButton", TEXT_BRIGHT)
	_variation(theme, "AccentButton", "Button")
	theme.set_stylebox("normal", "AccentButton", box(ACCENT, Color.TRANSPARENT, [0, 0, 0, 0], 3, Vector2(10, 3)))
	theme.set_stylebox("hover", "AccentButton", box(Color("1c8ae0"), Color.TRANSPARENT, [0, 0, 0, 0], 3, Vector2(10, 3)))
	theme.set_stylebox("pressed", "AccentButton", box(Color("0068b8"), Color.TRANSPARENT, [0, 0, 0, 0], 3, Vector2(10, 3)))
	theme.set_color("font_color", "AccentButton", TEXT_BRIGHT)
	theme.set_color("icon_normal_color", "AccentButton", TEXT_BRIGHT)
	_variation(theme, "GroupButton", "Button")
	for state in ["normal", "hover", "pressed", "hover_pressed", "focus"]:
		theme.set_stylebox(state, "GroupButton", box(Color.TRANSPARENT, BORDER, [0, 0, 0, 1], 0, Vector2(2, 4)))
	theme.set_font("font", "GroupButton", bold_font)
	theme.set_font_size("font_size", "GroupButton", SMALL_FONT_SIZE)
	theme.set_color("font_color", "GroupButton", TEXT_DIM)
	theme.set_color("font_hover_color", "GroupButton", TEXT)
	theme.set_color("font_pressed_color", "GroupButton", TEXT_DIM)
	theme.set_color("font_hover_pressed_color", "GroupButton", TEXT)
	theme.set_color("icon_normal_color", "GroupButton", TEXT_DIM)
	theme.set_constant("h_separation", "GroupButton", 4)
	_variation(theme, "StatusButton", "Button")
	for state in ["normal", "focus", "disabled"]:
		theme.set_stylebox(state, "StatusButton", _empty(Vector2(6, 2)))
	theme.set_stylebox("hover", "StatusButton", box(HOVER, Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2(6, 2)))
	theme.set_stylebox("pressed", "StatusButton", box(PRESSED, Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2(6, 2)))
	theme.set_font_size("font_size", "StatusButton", 12)
	theme.set_color("font_color", "StatusButton", TEXT)


static func _variation(theme: Theme, name: String, base: String) -> void:
	theme.set_type_variation(name, base)

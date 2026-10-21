extends Control
## The game editor, laid out like a code editor: a menu bar; on the left an outline of the game (or the recipes,
## or the building blocks); in the middle the table (drag zones, or Play), the deck or the game file's text, with
## the problems below; the selected part's settings on the right (Inspector); and a status bar. Every change edits
## the game file's text in place (GameDocument), so comments and layout survive; nothing is written until Save.

const MAIN_SCENE := preload("res://scenes/main.tscn")
const TITLE_SCENE := "res://scenes/title.tscn"
const SECTIONS := ["Game", "Table", "Deck", "Players", "Variables", "Actions", "Turn", "Triggers", "Conditions", "Scoring", "Melds"]
## Changes to the same setting within this long count as one step for Undo (e.g. dragging a number).
const MERGE_TIME := 1500
const RELOAD_DELAY := 0.3
const MESSAGE_TIME := 5.0
const DECKS_FOLDER := "res://decks/"
const NEW_DECK := "name,count\nCard 1,1\n"
## Sections listing named entries: where they live in the file, what one is called, the name a new one gets, and
## what it holds.
const NAMED := {
	"Table": {key = "zones", noun = "zone", first = "new_zone", new = {"layout": "pile", "anchor": [0.5, 0.5]}},
	"Actions": {key = "actions", noun = "action", first = "New action", new = {"steps": []}},
	"Triggers": {key = "triggers", noun = "trigger", first = "new_trigger", new = []},
	"Conditions": {key = "conditions", noun = "condition", first = "new_condition", new = "true"},
}
## Which section each part of the file is edited in.
const SECTION_OF := {
	"title": "Game", "description": "Game", "zones": "Table", "card_face": "Deck", "variables": "Variables",
	"player_variables": "Variables", "team_variables": "Variables", "constants": "Variables", "actions": "Actions", "turn": "Turn",
	"triggers": "Triggers", "conditions": "Conditions", "players": "Players", "player_names": "Players", "seats": "Players",
	"teams": "Players", "bot": "Players", "status": "Scoring", "show_variables": "Scoring", "melds": "Melds",
}
const TABLE_TAB := 0
const DECK_TAB := 1
const FILE_TAB := 2
const TABS := [["Table", "table"], ["Deck", "deck"], ["Game file", "file"]]
const SIDEBAR_PAGES := [["Outline", "outline", "OutlinePage"], ["Recipes", "recipes", "RecipesPage"], ["Building blocks", "blocks", "BlocksPage"]]

@export_file("*.jsonc", "*.json") var file_path := "res://games/color_match.jsonc"

var document: GameDocument
## Decks edited here, by path, until saved.
var deck_texts: Dictionary = {}
var history: Array[Dictionary] = []
var future: Array[Dictionary] = []
var saved_state: Dictionary
var last_change_path: Array = []
var last_change_time := 0
var section := ""
var entry := ""
var playing := false
var preview: Control
var dragger: ZoneDragger
var deck_table := DeckTable.new()
var palette := CommandPalette.new()
var reload_timer := Timer.new()
var message_timer := Timer.new()
var recipe_added := AcceptDialog.new()
## The game as last checked, for suggesting names and checking steps as they're typed.
var checked_game: Game
var problems: Array = []
## The deck shown in the Deck tab.
var deck_shown := ""
## The text box (a step, or the game file) last typed in, where building blocks are inserted.
var last_code: CodeEdit
## Every menu command, for the command palette: {text, detail, icon, run}.
var commands: Array = []
## The window's scaling before the editor opened, put back when it closes.
var window_scaling: Array = []


func _ready() -> void:
	theme = EditorTheme.build()
	_use_screen_pixels()
	document = GameDocument.open(file_path)
	saved_state = _state()
	%Inspector.setup(self)
	_build_menus()
	_build_bars()
	_build_sidebar()
	_build_center()
	recipe_added.dialog_autowrap = true
	recipe_added.min_size.x = 440
	add_child(recipe_added)
	add_child(palette)
	reload_timer.one_shot = true
	reload_timer.timeout.connect(_reload)
	add_child(reload_timer)
	message_timer.one_shot = true
	message_timer.timeout.connect(func() -> void: %StatusMessage.text = "")
	add_child(message_timer)
	get_viewport().gui_focus_changed.connect(func(control: Control) -> void:
		if control is CodeEdit:
			last_code = control)
	checked_game = Game.new(file_path, 1, _files())
	select_section("Table")
	_reload()
	_update_title()


func _exit_tree() -> void:
	get_window().content_scale_mode = window_scaling[0]
	get_window().content_scale_factor = window_scaling[1]


## The game scales its table with the window; the editor instead stays at the screen's own size, like any other
## application, so a bigger window shows more rather than bigger.
func _use_screen_pixels() -> void:
	var window := get_window()
	window_scaling = [window.content_scale_mode, window.content_scale_factor]
	window.content_scale_mode = Window.CONTENT_SCALE_MODE_DISABLED
	window.content_scale_factor = maxf(1.0, snappedf(DisplayServer.screen_get_dpi() / 96.0, 0.25))


func _unhandled_key_input(event: InputEvent) -> void:
	# Ctrl+Z and Ctrl+Y are the Edit menu's; this catches Ctrl+Shift+Z.
	if event.is_action_pressed("ui_redo"):
		redo()


# --- Building the screen ----------------------------------------------------------------------------------

func _build_menus() -> void:
	var recipes := PopupMenu.new()
	for recipe: String in Recipes.paths():
		recipes.add_item(Recipes.title(recipe))
	recipes.index_pressed.connect(func(index: int) -> void: add_recipe(Recipes.paths()[index]))
	_add_menu("File", [
		["Save", "save", KEY_MASK_CTRL | KEY_S, save],
		["Revert to saved", "restart", 0, _revert],
		[],
		["Open in text editor", "file", 0, func() -> void: OS.shell_open(ProjectSettings.globalize_path(file_path))],
		[],
		["Back to games", "back", KEY_MASK_CTRL | KEY_W, _leave],
	])
	_add_menu("Edit", [
		["Undo", "undo", KEY_MASK_CTRL | KEY_Z, undo],
		["Redo", "redo", KEY_MASK_CTRL | KEY_Y, redo],
		[],
		["Rename selected", "rename", 0, func() -> void: %Outline.start_rename()],
		["Duplicate selected", "duplicate", KEY_MASK_CTRL | KEY_D, func() -> void: if section in NAMED and entry: _duplicate(section, entry)],
		["Delete selected", "trash", 0, func() -> void: if section in NAMED and entry: _remove(section, entry)],
		[],
		["Command palette", "search", KEY_MASK_CTRL | KEY_P, open_palette],
	])
	_add_menu("Add", _add_items() + [[], ["Recipe", recipes]])
	_add_menu("View", [
		["Outline", "outline", KEY_MASK_CTRL | KEY_MASK_SHIFT | KEY_E, _show_sidebar_page.bind(0)],
		["Recipes", "recipes", KEY_MASK_CTRL | KEY_MASK_SHIFT | KEY_R, _show_sidebar_page.bind(1)],
		["Building blocks", "blocks", KEY_MASK_CTRL | KEY_MASK_SHIFT | KEY_B, _show_sidebar_page.bind(2)],
		[],
		["Table", "table", KEY_MASK_CTRL | KEY_1, show_tab.bind(TABLE_TAB)],
		["Deck", "deck", KEY_MASK_CTRL | KEY_2, show_tab.bind(DECK_TAB)],
		["Game file", "file", KEY_MASK_CTRL | KEY_3, show_tab.bind(FILE_TAB)],
		[],
		["Show or hide sidebar", "panel_left", KEY_MASK_CTRL | KEY_B, func() -> void: %Sidebar.visible = not %Sidebar.visible],
		["Show or hide inspector", "panel_right", KEY_MASK_CTRL | KEY_MASK_ALT | KEY_B, func() -> void: %Inspector.visible = not %Inspector.visible],
		["Show or hide problems", "panel_bottom", KEY_MASK_CTRL | KEY_J, _toggle_problems],
	])
	_add_menu("Game", [
		["Play or stop", "play", KEY_F5, func() -> void: set_playing(not playing)],
		["Restart", "restart", KEY_MASK_CTRL | KEY_R, _restart],
	])
	_add_menu("Help", [
		["Tutorial", "book", 0, _open_doc.bind("res://README.md")],
		["Game file reference", "book", KEY_F1, _open_doc.bind("res://docs/reference.md")],
	])


## The things the Add menu (and the outline's + button) can add.
func _add_items() -> Array:
	return [
		["Zone", "zone", 0, add_entry.bind("Table")],
		["Action", "actions", 0, add_entry.bind("Actions")],
		["Trigger", "triggers", 0, add_entry.bind("Triggers")],
		["Condition", "conditions", 0, add_entry.bind("Conditions")],
		["Variable", "variables", 0, _add_variable],
		["Deck", "deck", 0, _new_deck],
	]


func _add_variable() -> void:
	select_section("Variables")
	%Inspector.add_table_entry("variables")


## A menu from rows of [label, icon, shortcut, what it does], [] for a line, or [label, submenu].
func _add_menu(title: String, rows: Array) -> void:
	var menu := _popup(rows, title)
	menu.name = title
	%Menus.add_child(menu)


func _popup(rows: Array, title := "") -> PopupMenu:
	var menu := PopupMenu.new()
	var actions := []
	for row: Array in rows:
		if row.is_empty():
			menu.add_separator()
		elif row[1] is PopupMenu:
			row[1].name = row[0]
			menu.add_child(row[1])
			menu.add_submenu_node_item(row[0], row[1])
		else:
			menu.add_icon_item(EditorIcons.icon(row[1]), row[0], actions.size(), row[2])
			actions.append(row[3])
			if title:
				var shortcut := OS.get_keycode_string(row[2]) if row[2] else ""
				commands.append({text = row[0], detail = title + ("  " + shortcut if shortcut else ""), icon = row[1], run = row[3]})
	menu.id_pressed.connect(func(id: int) -> void: actions[id].call())
	return menu


func _build_bars() -> void:
	for button: Array in [[%BackButton, "back", "Back to games (Ctrl+W)", _leave], [%UndoButton, "undo", "Undo (Ctrl+Z)", undo],
			[%RedoButton, "redo", "Redo (Ctrl+Y)", redo], [%SaveButton, "save", "Save (Ctrl+S)", save],
			[%RestartButton, "restart", "Restart the game (Ctrl+R)", _restart]]:
		button[0].icon = EditorIcons.icon(button[1])
		button[0].tooltip_text = button[2]
		button[0].pressed.connect(button[3])
	%PlayButton.pressed.connect(func() -> void: set_playing(not playing))
	%ProblemStatus.pressed.connect(_toggle_problems)
	%PaletteStatus.pressed.connect(open_palette)
	%CloseProblems.icon = EditorIcons.icon("close", Color.WHITE, 0.85)
	%CloseProblems.pressed.connect(_toggle_problems)
	%Problems.item_activated.connect(func() -> void: _go_to_problem(%Problems.get_selected().get_metadata(0)))
	%Problems.item_selected.connect(func() -> void:
		# A right-click selects too, but it's for the copy menu, not for going there.
		if not Input.is_mouse_button_pressed(MOUSE_BUTTON_RIGHT):
			_go_to_problem(%Problems.get_selected().get_metadata(0)))
	var copy_menu := _popup([
		["Copy", "duplicate", KEY_MASK_CTRL | KEY_C, func() -> void: DisplayServer.clipboard_set(%Problems.get_selected().get_metadata(0))],
		["Copy all problems", "duplicate", 0, func() -> void: DisplayServer.clipboard_set("\n".join(problems))],
	])
	add_child(copy_menu)
	%Problems.item_mouse_selected.connect(func(at: Vector2, button: int) -> void:
		if button == MOUSE_BUTTON_RIGHT and problems:
			copy_menu.popup(Rect2i(Vector2i(%Problems.get_global_transform() * at), Vector2i.ZERO)))
	%Problems.gui_input.connect(func(event: InputEvent) -> void:
		if event is InputEventKey and event.pressed and event.keycode == KEY_C and event.ctrl_pressed and %Problems.get_selected():
			DisplayServer.clipboard_set(%Problems.get_selected().get_metadata(0))
			%Problems.accept_event())
	_show_mode()


func _build_sidebar() -> void:
	var group := ButtonGroup.new()
	group.allow_unpress = true
	for i in SIDEBAR_PAGES.size():
		var button := Button.new()
		button.theme_type_variation = &"ActivityButton"
		button.icon = EditorIcons.icon(SIDEBAR_PAGES[i][1], Color.WHITE, 1.4)
		button.tooltip_text = SIDEBAR_PAGES[i][0]
		button.toggle_mode = true
		button.button_group = group
		button.focus_mode = Control.FOCUS_NONE
		button.pressed.connect(func() -> void:
			%Sidebar.visible = button.button_pressed
			if button.button_pressed:
				_show_sidebar_page(i))
		%Activities.add_child(button)
	%Activities.get_child(0).button_pressed = true
	var add_menu := _popup(_add_items())
	add_child(add_menu)
	%AddButton.pressed.connect(func() -> void:
		add_menu.popup(Rect2i(Vector2i(%AddButton.global_position + Vector2(0, %AddButton.size.y)), Vector2i.ZERO)))
	%AddButton.icon = EditorIcons.icon("plus")
	%AddButton.tooltip_text = "Add a zone, action, trigger..."
	%OutlineFilter.right_icon = EditorIcons.icon("search", EditorTheme.TEXT_DIM)
	%OutlineFilter.text_changed.connect(func(text: String) -> void:
		%Outline.filter = text
		_fill_outline())
	%Outline.picked.connect(_pick)
	%Outline.add_asked.connect(add_entry)
	%Outline.rename_asked.connect(_rename)
	%Outline.duplicate_asked.connect(_duplicate)
	%Outline.remove_asked.connect(_remove)
	%RecipesPage.add_asked.connect(add_recipe)
	%BlocksPage.insert_asked.connect(_insert_block)
	_show_sidebar_page(0)


func _build_center() -> void:
	for tab: Array in TABS:
		%CenterTabs.add_tab(tab[0], EditorIcons.icon(tab[1], Color.WHITE, 0.85))
	%CenterTabs.tab_changed.connect(_on_tab_changed)
	%AddZoneButton.icon = EditorIcons.icon("plus", Color.WHITE, 0.85)
	%AddZoneButton.pressed.connect(add_entry.bind("Table"))
	preview = MAIN_SCENE.instantiate()
	preview.rules_path = file_path
	preview.embedded = true
	preview.given_files = _files()
	# The table looks as it does when played, not in the editor's own style.
	preview.theme = ThemeDB.get_default_theme()
	preview.set_anchors_preset(Control.PRESET_TOP_LEFT)
	%Center.add_child(preview)
	%Center.resized.connect(_fit_preview)
	preview.show_play_controls(false)
	var table: TableView = preview.get_node("%TableView")
	dragger = ZoneDragger.new(table)
	dragger.zone_picked.connect(func(zone: Zone) -> void: _pick("Table", zone.kind))
	dragger.zone_moved.connect(_move_zone)
	table.add_child(dragger)
	deck_table.set_anchors_preset(Control.PRESET_FULL_RECT)
	deck_table.edited.connect(_deck_edited)
	deck_table.preview_spot.theme = ThemeDB.get_default_theme()
	%DeckPage.add_child(deck_table)
	%FileView.edited.connect(_file_edited)
	show_tab(TABLE_TAB)


func _show_sidebar_page(index: int) -> void:
	%Sidebar.visible = true
	%Activities.get_child(index).button_pressed = true
	%SidebarTitle.text = SIDEBAR_PAGES[index][0].to_upper()
	for i in SIDEBAR_PAGES.size():
		get_node("%" + SIDEBAR_PAGES[i][2]).visible = i == index
	%AddButton.visible = index == 0


func show_tab(index: int) -> void:
	%CenterTabs.current_tab = index
	_on_tab_changed(index)


func _on_tab_changed(index: int) -> void:
	%Center.visible = index == TABLE_TAB
	%DeckPage.visible = index == DECK_TAB
	%FileView.visible = index == FILE_TAB
	%TableTools.visible = index == TABLE_TAB and not playing
	if index == DECK_TAB:
		_show_deck()
	elif index == FILE_TAB:
		%FileView.show_text(document.text)
		_go_to_in_file()


func _toggle_problems() -> void:
	%BottomPanel.visible = not %BottomPanel.visible


func _open_doc(path: String) -> void:
	OS.shell_open(ProjectSettings.globalize_path(path))


## The game is laid out at the play screen's size and shrunk to fit, so it looks as it will when played
## (only smaller) instead of crowding full-size cards together.
func _fit_preview() -> void:
	var play_size := Vector2(ProjectSettings.get_setting("display/window/size/viewport_width"), ProjectSettings.get_setting("display/window/size/viewport_height"))
	var shrink := minf(1.0, minf(%Center.size.x / play_size.x, %Center.size.y / play_size.y))
	preview.size = play_size
	preview.scale = Vector2(shrink, shrink)
	preview.position = (%Center.size - play_size * shrink) / 2


# --- Changing the game ------------------------------------------------------------------------------------

## Every edit from the forms comes through here, so it can be undone. A null value takes the key out.
func change(path: Array, value: Variant) -> void:
	_remember(path)
	if value == null:
		document.remove(path)
	else:
		document.set_value(path, value)
	_changed(false)


## Bigger edits (adding, renaming, removing), as a function that changes the document.
func edit(how: Callable) -> void:
	# Menu buttons don't take focus, so a box being typed in hasn't sent its text yet; it would be lost on rebuild.
	get_viewport().gui_release_focus()
	_remember([])
	how.call()
	_changed(true)


## Adds a ready-made piece of game (Recipes), then says what it does and what to adjust.
func add_recipe(recipe: String) -> void:
	edit(func() -> void: Recipes.add_to(document, recipe))
	recipe_added.title = "Added: " + Recipes.title(recipe)
	recipe_added.dialog_text = Recipes.description(recipe)
	recipe_added.popup_centered()


func undo() -> void:
	if history:
		future.append(_state())
		_restore(history.pop_back())


func redo() -> void:
	if future:
		history.append(_state())
		_restore(future.pop_back())


func save() -> void:
	get_viewport().gui_release_focus()
	if document.save() != OK:
		show_message("Couldn't save " + file_path)
		return
	for path: String in deck_texts:
		var file := FileAccess.open(path, FileAccess.WRITE)
		file.store_string(deck_texts[path])
		file.close()
	saved_state = _state()
	_update_title()
	show_message("Saved " + file_path.get_file())


func _revert() -> void:
	if _state() != saved_state:
		_remember([])
		_restore(saved_state)
		show_message("Back to the saved file (Ctrl+Z brings your changes back)")


func _remember(path: Array) -> void:
	var now := Time.get_ticks_msec()
	if path.is_empty() or path != last_change_path or now - last_change_time > MERGE_TIME:
		history.append(_state())
		future.clear()
	last_change_path = path
	last_change_time = now


func _state() -> Dictionary:
	return {rules = document.text, decks = deck_texts.duplicate()}


func _restore(state: Dictionary) -> void:
	document.set_text(state.rules)
	deck_texts = state.decks.duplicate()
	last_change_path = []
	if entry and not _entries(section).any(func(pair: Array) -> bool: return pair[0] == entry):
		entry = ""
	_changed(true)


## After any change: the outline, title and game file catch up, and the table and problems are checked again
## shortly.
func _changed(rebuild_details: bool) -> void:
	_update_title()
	_fill_outline()
	if rebuild_details:
		%Inspector.show_page()
	if %FileView.visible:
		%FileView.show_text(document.text)
	if %DeckPage.visible and rebuild_details:
		_show_deck()
	reload_timer.start(RELOAD_DELAY)


func _file_edited(text: String) -> void:
	_remember(["game file"])
	document.set_text(text)
	_changed(true)


func _update_title() -> void:
	var unsaved := _state() != saved_state
	var title: String = document.get_value(["title"], "") if document.get_value(["title"]) is String else ""
	%FileLabel.text = (title + "  ·  " if title else "") + file_path.get_file() + ("  ●" if unsaved else "")
	%SaveStatus.text = "Unsaved changes" if unsaved else "Saved"
	%SaveStatus.add_theme_color_override("font_color", EditorTheme.TEXT if unsaved else EditorTheme.TEXT_DIM)
	var counts := []
	for part: Array in [["zones", "zone"], ["actions", "action"], ["triggers", "trigger"]]:
		var value: Variant = document.get_value([part[0]], {})
		var count: int = value.size() if value is Dictionary else 0
		counts.append("%d %s%s" % [count, part[1], "" if count == 1 else "s"])
	var players: Variant = document.get_value(["players"], 1)
	counts.append("%s player%s" % [GameDocument.one_line(players), "" if str(players) == "1" else "s"])
	%SummaryStatus.text = "  ·  ".join(counts)


func _files() -> Dictionary:
	var files := {file_path: document.text}
	files.merge(deck_texts, true)
	return files


func _reload() -> void:
	preview.given_files = _files()
	preview.load_game()
	dragger.queue_redraw()
	if document.parse_error:
		_show_problems(["The game file can't be read: " + document.parse_error])
	else:
		checked_game = Game.new(file_path, 1, _files())
		_show_problems(checked_game.errors)
	var line := RegEx.create_from_string("^line (\\d+)").search(document.parse_error)
	%FileView.show_error_line(line.get_string(1).to_int() - 1 if line else -1)


func _show_problems(found: Array) -> void:
	problems = found
	%Problems.clear()
	var root: TreeItem = %Problems.create_item()
	for problem: String in found:
		var item: TreeItem = %Problems.create_item(root)
		item.set_text(0, problem)
		item.set_icon(0, EditorIcons.icon("error", EditorTheme.ERROR))
		item.set_metadata(0, problem)
		item.set_tooltip_text(0, problem)
	if found.is_empty():
		var none: TreeItem = %Problems.create_item(root)
		none.set_text(0, "No problems found.")
		none.set_icon(0, EditorIcons.icon("ok", EditorTheme.GOOD))
		none.set_selectable(0, false)
		none.set_metadata(0, "")
	%ProblemCount.text = str(found.size()) if found else ""
	%ProblemStatus.text = "%d problem%s" % [found.size(), "" if found.size() == 1 else "s"] if found else "No problems"
	%ProblemStatus.icon = EditorIcons.icon("error" if found else "ok", EditorTheme.ERROR if found else EditorTheme.TEXT, 0.8)
	_fill_outline()


## A short note in the status bar that goes away by itself.
func show_message(text: String) -> void:
	%StatusMessage.text = text
	message_timer.start(MESSAGE_TIME)


func set_playing(on: bool) -> void:
	playing = on
	dragger.visible = not on
	preview.show_play_controls(on)
	if on:
		show_tab(TABLE_TAB)
	%TableTools.visible = %CenterTabs.current_tab == TABLE_TAB and not on
	_show_mode()
	_reload()


func _restart() -> void:
	if not playing:
		set_playing(true)
	else:
		_reload()


func _show_mode() -> void:
	%PlayButton.text = "Stop" if playing else "Play"
	%PlayButton.icon = EditorIcons.icon("stop" if playing else "play")
	%PlayButton.tooltip_text = ("Stop playing" if playing else "Play the game as it is now, saved or not") + " (F5)"
	%RestartButton.visible = playing
	%ModeStatus.text = "Playing" if playing else "Editing"
	%ModeStatus.icon = EditorIcons.icon("play" if playing else "rename", Color.WHITE, 0.75)
	%StatusBar.add_theme_stylebox_override("panel", EditorTheme.box(EditorTheme.PLAYING if playing else EditorTheme.PANEL,
		EditorTheme.BORDER, [0, 1, 0, 0], 0, Vector2(6, 0)))


func _leave() -> void:
	if _state() == saved_state:
		get_tree().change_scene_to_file(TITLE_SCENE)
		return
	var dialog := ConfirmationDialog.new()
	dialog.dialog_text = "Leave without saving your changes?"
	dialog.ok_button_text = "Leave"
	dialog.confirmed.connect(func() -> void: get_tree().change_scene_to_file(TITLE_SCENE))
	add_child(dialog)
	dialog.popup_centered()


# --- Sections and entries ---------------------------------------------------------------------------------

## Shows a section, with its first entry if it has any.
func select_section(name: String) -> void:
	section = name
	var entries := _entries(name)
	entry = entries[0][0] if entries else ""
	_selection_changed()


func select_entry(key: String) -> void:
	entry = key
	_selection_changed()


## Picked in the outline or on the table: a section row shows the section on its own (the Deck's first deck).
func _pick(picked_section: String, picked_entry: String) -> void:
	if picked_section == "Deck" and picked_entry.is_empty():
		select_section("Deck")
		return
	section = picked_section
	entry = picked_entry
	_selection_changed()


func _selection_changed() -> void:
	%Outline.show_selected(section, entry)
	dragger.selected_kind = entry if section == "Table" else ""
	dragger.queue_redraw()
	if section == "Deck" and entry:
		deck_shown = entry
		if %CenterTabs.current_tab != FILE_TAB:
			show_tab(DECK_TAB)
	elif section == "Table" and %CenterTabs.current_tab == DECK_TAB:
		show_tab(TABLE_TAB)
	elif %CenterTabs.current_tab == FILE_TAB:
		_go_to_in_file()
	%Inspector.show_page()


## The section's entries as [key, label, detail].
func _entries(of_section: String) -> Array:
	if of_section in NAMED:
		var values: Variant = document.get_value([NAMED[of_section].key], {})
		return values.keys().map(func(name: String) -> Array: return [name, name, _detail(of_section, values[name])]) if values is Dictionary else []
	if of_section == "Deck":
		return all_decks().map(func(deck: String) -> Array: return [deck, deck.get_file()])
	return []


## A word or two shown beside an entry in the outline: a zone's layout and copies, how an action is done.
static func _detail(of_section: String, value: Variant) -> String:
	if not value is Dictionary:
		return "button" if of_section == "Actions" else ""
	if of_section == "Table":
		var copies := " ×P" if value.get("per_player", false) else (" ×%d" % value.copies if value.get("copies") is float else "")
		return str(value.get("layout", "row")) + copies
	if of_section == "Actions":
		for way in ["drag", "click", "double_click"]:
			if value.has(way):
				return way.replace("_", " ")
		return "button" if value.get("button", true) else "ask"
	return ""


func _fill_outline() -> void:
	var sections := []
	for name: String in SECTIONS:
		var part := {name = name, entries = _entries(name)}
		if name in NAMED:
			part.noun = NAMED[name].noun
			part.count = part.entries.size()
		sections.append(part)
	var marks := {}
	for problem: String in problems:
		var place := _place_of(problem)
		if place:
			marks[place[0] + ("/" + place[1] if place[1] else "")] = problem
			marks[place[0]] = problem
	%Outline.show_sections(sections, marks, section, entry)


func decks_used() -> Array:
	var decks := []
	var zones: Variant = document.get_value(["zones"], {})
	for zone: Variant in zones.values() if zones is Dictionary else []:
		if zone is Dictionary and zone.get("cards") is String and zone.cards not in decks:
			decks.append(zone.cards)
	return decks


## The decks the zones use, and any new ones made here.
func all_decks() -> Array:
	var decks := decks_used()
	for deck: String in deck_texts:
		if deck not in decks:
			decks.append(deck)
	return decks


## Adds an entry with a name not used yet, then lets you type its real name in the outline.
func add_entry(to_section: String) -> void:
	var named: Dictionary = NAMED[to_section]
	var name: String = named.first
	var number := 1
	while document.has([named.key, name]):
		number += 1
		name = "%s_%d" % [named.first, number] if named.first.is_valid_identifier() else "%s %d" % [named.first, number]
	section = to_section
	edit(func() -> void: document.set_value([named.key, name], named.new))
	select_entry(name)
	_show_sidebar_page(0)
	%Outline.start_rename()


func _rename(of_section: String, old: String, new_name: String) -> void:
	var key: String = NAMED[of_section].key
	if not _good_name(of_section, new_name):
		show_message("%s names must be one word: letters, numbers and _." % NAMED[of_section].noun.capitalize())
		_fill_outline()
		return
	if document.has([key, new_name]):
		show_message("There's already a %s called %s." % [NAMED[of_section].noun, new_name])
		_fill_outline()
		return
	section = of_section
	edit(func() -> void: document.rename_key([key, old], new_name))
	select_entry(new_name)
	if RegEx.create_from_string("\\b%s\\b" % old).search(document.text):
		show_message("Renamed. Steps that still say %s need changing too." % old)


func _duplicate(of_section: String, key: String) -> void:
	var named: Dictionary = NAMED[of_section]
	var copy := key + ("_copy" if key.is_valid_identifier() else " copy")
	while document.has([named.key, copy]):
		copy += "_copy" if key.is_valid_identifier() else " copy"
	section = of_section
	edit(func() -> void: document.set_value([named.key, copy], document.get_value([named.key, key])))
	select_entry(copy)


func _remove(of_section: String, key: String) -> void:
	section = of_section
	entry = ""
	edit(func() -> void: document.remove([NAMED[of_section].key, key]))
	_selection_changed()
	show_message("Deleted %s %s (Ctrl+Z to undo)" % [NAMED[of_section].noun, key])


## Zone, trigger and condition names are used in steps, so they must be single words; actions can say anything.
func _good_name(of_section: String, name: String) -> bool:
	if of_section == "Actions":
		return not name.is_empty()
	if of_section == "Conditions":
		return GameRules.condition_name_pattern.search(name) != null
	return name.is_valid_identifier()


## A new deck file (written on Save), for a zone's cards.
func _new_deck() -> void:
	var base := DECKS_FOLDER + file_path.get_file().get_basename() + "_deck"
	var path := base + ".csv"
	var number := 1
	while FileAccess.file_exists(path) or path in deck_texts:
		number += 1
		path = "%s_%d.csv" % [base, number]
	_remember([])
	deck_texts[path] = NEW_DECK
	_changed(false)
	section = "Deck"
	select_entry(path)
	show_message("New deck %s: give it to a zone as its cards, under Table." % path.get_file())


func _show_deck() -> void:
	var decks := all_decks()
	if deck_shown not in decks:
		deck_shown = decks[0] if decks else ""
	deck_table.visible = not deck_shown.is_empty()
	%NoDeck.visible = deck_shown.is_empty()
	if deck_shown:
		var text: String = deck_texts.get(deck_shown, FileAccess.get_file_as_string(deck_shown))
		deck_table.show_deck(text, document.get_value(["card_face"], GameRules.DEFAULT_CARD_FACE))


# --- Steps, the deck and the table ------------------------------------------------------------------------

func smart_field(text: String) -> SmartField:
	if not checked_game:
		return SmartField.new(text, [], deck_columns(), _check_step)
	var rules := checked_game.rules
	var names: Array = Array(checked_game.input_names()) + rules.conditions.keys()
	var plain_conditions := rules.conditions.keys().filter(func(name: String) -> bool: return rules.condition_parameters[name].is_empty())
	var for_players: Array = rules.player_variables.keys() + plain_conditions
	var actions := rules.actions.keys()
	var quoted := {
		"offer": actions, "offer_until": actions, "ask": actions, "ask_all": actions + rules.triggers.keys(),
		"ask_others": actions + rules.triggers.keys(), "trigger": rules.triggers.keys(), "matching": plain_conditions,
		"most": for_players, "least": for_players, "players_with_most": for_players, "players_with_least": for_players,
		"players_where": for_players, "reset": rules.variables.keys() + rules.player_variables.keys() + rules.team_variables.keys(),
	}
	var signatures := {}
	for name: String in rules.conditions:
		if rules.condition_parameters[name]:
			signatures[name] = "%s(%s)" % [name, ", ".join(rules.condition_parameters[name])]
	return SmartField.new(text, names, deck_columns(), _check_step, quoted, signatures)


func _check_step(text: String) -> Array:
	return StepCompiler.new(checked_game).problems_with(text) if checked_game else []


func deck_columns() -> Array:
	var columns := []
	for deck: String in all_decks():
		var rows := DeckLoader.rows(deck_texts.get(deck, FileAccess.get_file_as_string(deck)))
		for column in rows[0] if rows else PackedStringArray():
			if column not in columns:
				columns.append(column)
	return columns


## A building block picked in the sidebar goes where a step (or the game file) was last being typed.
func _insert_block(text: String) -> void:
	if is_instance_valid(last_code) and last_code.is_visible_in_tree():
		last_code.grab_focus()
		last_code.insert_text_at_caret(text)
	else:
		DisplayServer.clipboard_set(text)
		show_message("Copied %s: click into a step and paste it." % text)


func open_palette() -> void:
	var entries := commands.duplicate()
	for name: String in SECTIONS:
		entries.append({text = name, detail = "Go to", icon = GameOutline.ICONS[name], run = _pick.bind(name, "")})
		for row: Array in _entries(name):
			entries.append({text = row[1], detail = name, icon = GameOutline.ICONS[name], run = _pick.bind(name, row[0])})
	for recipe: String in Recipes.paths():
		entries.append({text = "Add recipe: " + Recipes.title(recipe), detail = "Recipes", icon = "recipes", run = add_recipe.bind(recipe)})
	palette.open(entries)


func _deck_edited(text: String) -> void:
	_remember(["deck", deck_shown])
	deck_texts[deck_shown] = text
	_changed(false)


## Dragging a zone on the table: per-player zones placed round the table move seat 1's spot, grids move as a
## whole, and zones placed one by one ("anchors") move just that one.
func _move_zone(zone: Zone, spot: Vector2) -> void:
	var path: Array = ["zones", zone.kind]
	var definition: Dictionary = document.get_value(path, {})
	var number := int(zone.name.trim_prefix(zone.kind + "_")) if zone.name != zone.kind else 0
	var players: int = preview.game.rules.players if preview.game else 1
	if number and definition.has("anchors"):
		path += ["anchors", number - 1]
	else:
		path.append("anchor")
		if number > 1 and definition.get("per_player", false):
			spot = Vector2(0.5, 0.5) + (spot - Vector2(0.5, 0.5)).rotated(-TAU * (number - 1) / players)
		elif number and definition.has("grid"):
			var start: Array = definition.get("anchor", [0.5, 0.5])
			spot = Vector2(start[0], start[1]) + spot - zone.anchor
	change(path, [snappedf(spot.x, 0.005), snappedf(spot.y, 0.005)])
	%Inspector.show_page()


# --- Problems and the game file ---------------------------------------------------------------------------

func _go_to_problem(problem: String) -> void:
	if problem.is_empty():
		return
	if document.parse_error:
		show_tab(FILE_TAB)
		%FileView.set_caret_line(maxi(%FileView.error_line, 0))
		%FileView.center_viewport_to_caret()
		%FileView.grab_focus()
		return
	var place := _place_of(problem)
	if place:
		_show_sidebar_page(0)
		_pick(place[0], place[1])


## Problems name what they're about: a zone, or a step's text, which is looked up in the file. Gives
## [section, entry], or [] if it can't tell.
func _place_of(problem: String) -> Array:
	var zone := RegEx.create_from_string("^Zone \"(\\w+)\"").search(problem)
	if zone:
		return ["Table", zone.get_string(1)]
	var quoted := RegEx.create_from_string("\"([^\"]+)\"").search(problem)
	var path := _path_holding(document.data, quoted.get_string(1), []) if quoted else []
	if path.is_empty() or path[0] not in SECTION_OF:
		return []
	var found_section: String = SECTION_OF[path[0]]
	return [found_section, str(path[1]) if path.size() >= 2 and found_section in NAMED else ""]


## The path of the first value or key in the file equal to the text.
func _path_holding(value: Variant, text: String, path: Array) -> Array:
	if value is String and value == text:
		return path
	if value is Dictionary:
		for key: String in value:
			if key == text:
				return path + [key]
			var found := _path_holding(value[key], text, path + [key])
			if found:
				return found
	if value is Array:
		for i in value.size():
			var found := _path_holding(value[i], text, path + [i])
			if found:
				return found
	return []


## In the Game file tab, shows the selected entry's place in the text.
func _go_to_in_file() -> void:
	var path: Array = []
	if section in NAMED:
		path = [NAMED[section].key] + ([entry] if entry else [])
	else:
		for key: String in SECTION_OF:
			if SECTION_OF[key] == section and document.has([key]):
				path = [key]
				break
	if path and document.has(path):
		%FileView.go_to(document.offset_of(path))

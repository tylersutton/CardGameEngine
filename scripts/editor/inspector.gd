class_name Inspector
extends PanelContainer
## The editor's right-hand panel: the selected part of the game (a zone, an action, the turn...) with its settings,
## and at the bottom a description of whatever setting is under the mouse or being typed in. Edits go through the
## editor (change and edit), so they can be undone.

const TRIGGER_HELP := {
	"start": "Runs when the game begins (and on Reload): deal the cards, set things up.",
	"turn_start": "Runs after every end_turn().",
}
const SECTION_HELP := {
	"Game": "The game's name and a line about it, both shown on the title screen.",
	"Table": "Zones are the places cards can be: decks, hands, piles. Drag a zone on the table to move it, or pick one to change it.",
	"Deck": "The cards, one row per kind of card (a count column says how many copies). The card face says which columns are drawn on a card.",
	"Players": "How many players there are, what they're called, and which seats the computer plays.",
	"Variables": "Values the game keeps track of, with their starting values. Use their names in any step.",
	"Actions": "What players can do. An action is done by a drag or click if it says so, otherwise by its button. For a drag or click, the first action in the file that allows it is done.",
	"Turn": "Steps run again and again for whoever's turn it is (end_turn() passes it on). offer(['Play a card', 'Draw']) waits for the player to do one of those actions; actions never offered can be done any time. Leave it empty for games without turns, like solitaire.",
	"Triggers": "Steps that run at the start, when a deck runs out, or when a step or card asks for them with trigger('name').",
	"Conditions": "Named expressions: write one once and use its name in any step.",
	"Scoring": "Live readouts in the side panel, recalculated after every change. Name players with name_of(1), and use viewer() for anything personal (\"your points\").",
	"Melds": "Which groups of cards count as melds; this turns on melds(), deadwood() and layoff().",
}
const VARIABLE_TABLES := [
	["variables", "Variables", "Shared values, with their starting values, e.g. phase \"draw\" or passes 0."],
	["player_variables", "Player variables", "One of each per player (score_1, score_2); in steps, score means the current player's."],
	["team_variables", "Team variables", "One of each per team (needs teams, under Players)."],
	["constants", "Constants", "Values that never change, e.g. a points table shell_points [0, 0, 2, 4, 6]: read them in steps."],
]
const NEW_MELD_TYPE := {"same": "rank", "min": 3}
const DESCRIPTION_HEIGHT := 78

var editor: Node
var document: GameDocument
var kind_label := Label.new()
var name_label := Label.new()
var header_icon := TextureRect.new()
var content := VBoxContainer.new()
var scroll := ScrollContainer.new()
var description_title := Label.new()
var description_text := Label.new()
## What the description says when no setting is being pointed at.
var page_title := ""
var page_help := ""
## A variable just added: its name box takes the focus once the page is built.
var name_to_focus := ""


func _ready() -> void:
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 0)
	add_child(column)
	var header := PanelContainer.new()
	header.theme_type_variation = &"Bar"
	var header_row := HBoxContainer.new()
	header_row.add_theme_constant_override("separation", 6)
	header_icon.stretch_mode = TextureRect.STRETCH_KEEP_CENTERED
	header_icon.modulate = EditorTheme.TEXT_DIM
	kind_label.theme_type_variation = &"HeaderLabel"
	name_label.theme_type_variation = &"TitleLabel"
	name_label.clip_text = true
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for part: Control in [header_icon, kind_label, name_label]:
		header_row.add_child(part)
	var header_margin := EditorTheme.padded(header_row, 10, 7)
	header.add_child(header_margin)
	column.add_child(header)
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content.add_theme_constant_override("separation", 6)
	scroll.add_child(EditorTheme.padded(content, 10, 8))
	column.add_child(scroll)
	var description := PanelContainer.new()
	description.add_theme_stylebox_override("panel", EditorTheme.box(EditorTheme.PANEL, EditorTheme.BORDER, [0, 1, 0, 0], 0, Vector2(10, 7)))
	description.custom_minimum_size.y = DESCRIPTION_HEIGHT
	var description_column := VBoxContainer.new()
	description_column.add_theme_constant_override("separation", 2)
	description_title.theme_type_variation = &"TitleLabel"
	description_text.theme_type_variation = &"DimLabel"
	description_text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	description_column.add_child(description_title)
	description_column.add_child(description_text)
	description.add_child(description_column)
	column.add_child(description)


func setup(game_editor: Node) -> void:
	editor = game_editor
	document = editor.document


## Shows a setting's help at the bottom, or the page's own when given nothing.
func describe(title := "", help := "") -> void:
	description_title.text = title if title else page_title
	description_text.text = help if help else page_help


# --- Pages ------------------------------------------------------------------------------------------------

## Shows the editor's selected section and entry.
func show_page() -> void:
	var section: String = editor.section
	var entry: String = editor.entry
	var keep_scroll := scroll.scroll_vertical
	for child in content.get_children():
		child.queue_free()
	header_icon.texture = EditorIcons.icon(GameOutline.ICONS[section])
	page_title = section
	page_help = SECTION_HELP[section]
	if section in editor.NAMED and entry:
		kind_label.text = editor.NAMED[section].noun.to_upper()
		name_label.text = entry
		page_title = entry
	elif section == "Deck" and entry:
		kind_label.text = "DECK"
		name_label.text = entry.get_file()
	else:
		kind_label.text = ""
		name_label.text = section
	if section in editor.NAMED and entry.is_empty():
		_show_overview(section)
	else:
		match section:
			"Game":
				_add_form([], EditorFields.GAME)
			"Table":
				_add_note(["zones", entry])
				_add_form(["zones", entry], EditorFields.ZONE)
			"Deck":
				_show_card_face(entry)
			"Actions":
				_show_action(entry)
			"Turn":
				_show_turn()
			"Triggers":
				page_help = TRIGGER_HELP.get(entry, "Runs when a step calls trigger('%s'), or when a card names it in an on_<zone> column (then card is that card)." % entry)
				_add_note(["triggers", entry])
				content.add_child(StepList.new(editor, ["triggers", entry]))
			"Conditions":
				page_help = "A named expression: write its name in any step or condition, or use matching(cards, '%s') to keep the cards it's true for (card is each card). A name like points(cards) takes parameters: use it as points(hand)." % entry
				_add_note(["conditions", entry])
				var field: SmartField = editor.smart_field(str(document.get_value(["conditions", entry], "")))
				field.committed.connect(func(text: String) -> void: editor.change(["conditions", entry], text))
				content.add_child(field)
			"Variables":
				for table: Array in VARIABLE_TABLES:
					_add_name_table(table[0], table[1], table[2])
			"Players":
				_add_form([], EditorFields.PLAYERS)
				_add_heading("Bots", "Which seats the computer plays, and how well.")
				_add_form(["bot"], EditorFields.BOT)
			"Scoring":
				_add_name_table("status", "Status", SECTION_HELP.Scoring, true)
				_add_form([], EditorFields.SCORING)
			"Melds":
				_show_melds()
	describe()
	scroll.set_deferred("scroll_vertical", keep_scroll)


## A section of named entries with none picked: what's in it, and a button to add one.
func _show_overview(section: String) -> void:
	var noun: String = editor.NAMED[section].noun
	var entries: Array = document.get_value([editor.NAMED[section].key], {}).keys()
	if entries.is_empty():
		var empty := Label.new()
		empty.text = "No %ss yet." % noun
		empty.theme_type_variation = &"DimLabel"
		content.add_child(empty)
	for name: String in entries:
		var row := Button.new()
		row.theme_type_variation = &"FlatButton"
		row.text = name
		row.alignment = HORIZONTAL_ALIGNMENT_LEFT
		row.pressed.connect(editor.select_entry.bind(name))
		content.add_child(row)
	var add := Button.new()
	add.theme_type_variation = &"AccentButton"
	add.text = "Add a " + noun
	add.icon = EditorIcons.icon("plus", Color.WHITE, 0.85)
	add.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	add.pressed.connect(editor.add_entry.bind(section))
	content.add_child(add)
	var help := Label.new()
	help.text = SECTION_HELP[section]
	help.theme_type_variation = &"DimLabel"
	help.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	content.add_child(help)


func _show_card_face(deck: String) -> void:
	if deck.is_empty():
		var none := Label.new()
		none.text = "No deck yet: pick a zone under Table and choose its cards."
		none.theme_type_variation = &"DimLabel"
		none.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		content.add_child(none)
		return
	_add_heading("Card face", "Which deck columns are drawn on each card, and its colors.")
	_add_form(["card_face"], EditorFields.CARD_FACE, editor.deck_columns())


## An action written as just a list of steps (a button) gets the full form once it needs more than steps.
func _show_action(entry: String) -> void:
	var path: Array = ["actions", entry]
	_add_note(path)
	if document.get_value(path) is Dictionary:
		_add_form(path, EditorFields.ACTION)
		return
	_add_heading("Steps", "What the button does.")
	content.add_child(StepList.new(editor, path))
	var more := Button.new()
	more.theme_type_variation = &"FlatButton"
	more.text = "Drag, click, choosing and more..."
	more.icon = EditorIcons.icon("plus", Color.WHITE, 0.8)
	more.tooltip_text = "Turn this button into a full action, with drag, click, when, choose and more"
	more.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	more.pressed.connect(func() -> void: editor.edit(func() -> void:
		var steps: Variant = document.get_value(path)
		document.set_value(path, {"steps": steps if steps is Array else [steps]})))
	content.add_child(more)


## The turn in words (TurnSummary), in a box above its steps.
func _show_turn() -> void:
	_add_note(["turn"])
	if editor.checked_game:
		var box := PanelContainer.new()
		box.theme_type_variation = &"Card"
		var summary := Label.new()
		summary.text = "\n".join(TurnSummary.lines(editor.checked_game.rules))
		summary.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		box.add_child(summary)
		content.add_child(box)
	_add_heading("Steps", SECTION_HELP.Turn)
	content.add_child(StepList.new(editor, ["turn"]))


## Rummy-style melds: a list of kinds of meld (sets, runs), each with its own small form.
func _show_melds() -> void:
	var types: Array = document.get_value(["melds", "types"], [])
	for i in types.size():
		var remove := _add_heading("Meld type %d" % (i + 1), "One kind of meld, e.g. a set (same rank) or a run (same suit, numbers in order).", "trash")
		remove.tooltip_text = "Remove meld type %d" % (i + 1)
		remove.pressed.connect(func() -> void: editor.edit(func() -> void: document.remove(["melds", "types", i])))
		_add_form(["melds", "types", i], EditorFields.MELD_TYPE, editor.deck_columns())
	var add := Button.new()
	add.theme_type_variation = &"FlatButton"
	add.text = "Add meld type"
	add.icon = EditorIcons.icon("plus", Color.WHITE, 0.8)
	add.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	add.pressed.connect(func() -> void: editor.edit(func() -> void:
		if document.has(["melds", "types"]):
			document.insert(["melds", "types"], types.size(), NEW_MELD_TYPE)
		else:
			document.set_value(["melds", "types"], [NEW_MELD_TYPE])))
	content.add_child(add)
	_add_heading("Deadwood", "Cards left out of melds.")
	_add_form(["melds"], EditorFields.MELDS, editor.deck_columns())


## Name and starting value of each variable; values are written as in the file (5, "draw", true, []).
## Readouts (status) instead pair any name with an expression.
func _add_name_table(key: String, title: String, help: String, readouts := false) -> void:
	var add := _add_heading(title, help, "plus")
	add.tooltip_text = "Add a readout" if readouts else "Add a variable"
	add.pressed.connect(add_table_entry.bind(key, readouts))
	var grid := GridContainer.new()
	grid.columns = 3
	for name: String in document.get_value([key], {}):
		var name_box := LineEdit.new()
		name_box.text = name
		name_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_box.size_flags_stretch_ratio = 0.7
		name_box.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
		var rename := func(_text: Variant = null) -> void:
			if name_box.text != name and _good_table_name(key, name_box.text, readouts):
				editor.edit(func() -> void: document.rename_key([key, name], name_box.text))
		name_box.text_submitted.connect(rename)
		name_box.focus_exited.connect(rename)
		var value_box: Control
		if readouts:
			value_box = editor.smart_field(str(document.get_value([key, name])))
			value_box.committed.connect(func(text: String) -> void: editor.change([key, name], text))
		else:
			var line := LineEdit.new()
			line.text = GameDocument.one_line(document.get_value([key, name]))
			line.placeholder_text = "starting value"
			var send := func(_text: Variant = null) -> void: editor.change([key, name], _value_from_text(line.text))
			line.text_submitted.connect(send)
			line.focus_exited.connect(send)
			value_box = line
		value_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		var remove := Button.new()
		remove.theme_type_variation = &"FlatButton"
		remove.icon = EditorIcons.icon("trash", Color.WHITE, 0.75)
		remove.tooltip_text = "Remove " + name
		remove.pressed.connect(func() -> void: editor.edit(func() -> void: document.remove([key, name])))
		for control: Control in [name_box, value_box, remove]:
			grid.add_child(control)
		if name == name_to_focus:
			name_to_focus = ""
			name_box.grab_focus.call_deferred()
			name_box.select_all.call_deferred()
	content.add_child(grid)


## Adds a variable (or readout) with a name not used yet, and puts the cursor in its name box.
func add_table_entry(key: String, readouts := false) -> void:
	var name := _unused_name(key, "Readout" if readouts else "variable")
	name_to_focus = name
	editor.edit(func() -> void: document.set_value([key, name], "''" if readouts else 0))


func _unused_name(key: String, base: String) -> String:
	var name := base
	var number := 1
	while document.has([key, name]):
		number += 1
		name = "%s_%d" % [base, number] if base.is_valid_identifier() else "%s %d" % [base, number]
	return name


## Variables are used in steps, so their names must be single words; readouts are labels and can say anything.
func _good_table_name(key: String, name: String, readouts: bool) -> bool:
	return (not name.is_empty() if readouts else name.is_valid_identifier()) and not document.has([key, name])


## 5, true, [] and "text" as written in the file; anything that isn't valid there is taken as text.
static func _value_from_text(text: String) -> Variant:
	var value: Variant = JSON.parse_string(text)
	return text if value == null and text.strip_edges() != "null" else value


func _add_form(path: Array, fields: Array, columns: Array = []) -> void:
	var form := EditorForm.new()
	form.show_fields(editor, path, fields, columns)
	form.changed.connect(func(changed_path: Array, value: Variant) -> void:
		var existed := document.has(changed_path)
		editor.change(changed_path, value)
		# A drag or click being added or taken out greys out the other, so the form is built again.
		var added_or_taken_out: bool = existed != document.has(changed_path) and changed_path.back() in ["drag", "click", "double_click"]
		if value == null or changed_path.back() in ["per_player", "copies", "cards"] or added_or_taken_out:
			show_page.call_deferred())
	form.described.connect(describe)
	content.add_child(form)


## A small heading, with its help in the description on hover. With an icon, it has a button on the right,
## which is returned.
func _add_heading(text: String, help: String, button_icon := "") -> Button:
	if content.get_children().any(func(child: Node) -> bool: return not child.is_queued_for_deletion()):
		var space := Control.new()
		space.custom_minimum_size.y = 6
		content.add_child(space)
	var row := HBoxContainer.new()
	var label := Label.new()
	label.text = text.to_upper()
	label.theme_type_variation = &"HeaderLabel"
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	label.tooltip_text = help
	label.mouse_filter = Control.MOUSE_FILTER_PASS
	row.add_child(label)
	row.mouse_entered.connect(describe.bind(text, help))
	content.add_child(row)
	if button_icon.is_empty():
		return null
	var button := Button.new()
	button.theme_type_variation = &"FlatButton"
	button.icon = EditorIcons.icon(button_icon, Color.WHITE, 0.8)
	row.add_child(button)
	return button


## The comment lines above an entry in the file, editable as a note; a small button until there is one.
func _add_note(path: Array) -> void:
	var note := TextEdit.new()
	note.placeholder_text = "Note (kept as a // comment in the file)"
	note.text = document.note(path)
	note.scroll_fit_content_height = true
	note.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	note.add_theme_color_override("font_color", EditorTheme.COMMENT)
	note.focus_exited.connect(func() -> void:
		if note.text != document.note(path):
			editor.edit(func() -> void: document.set_note(path, note.text)))
	note.visible = not note.text.is_empty()
	content.add_child(note)
	if note.visible:
		return
	var add := Button.new()
	add.theme_type_variation = &"FlatButton"
	add.text = "Add note"
	add.icon = EditorIcons.icon("note", Color.WHITE, 0.75)
	add.add_theme_color_override("font_color", EditorTheme.TEXT_DIM)
	add.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	add.tooltip_text = "A note about this, kept as a // comment in the game file"
	add.pressed.connect(func() -> void:
		add.visible = false
		note.visible = true
		note.grab_focus())
	content.add_child(add)
	content.move_child(add, note.get_index())

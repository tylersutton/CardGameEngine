class_name DeckTable
extends VBoxContainer
## The editor's deck: a table of cards (one row per kind of card, one column per CSV column) with the selected
## card drawn beside it using the game's card face. Every change is sent as edited(csv_text).

signal edited(csv_text: String)

const PREVIEW_SCALE := 2.0

var rows: Array[PackedStringArray] = []
var card_face: Dictionary = {}
var grid := Tree.new()
var preview_spot := Control.new()
var preview: CardView


func _init() -> void:
	add_theme_constant_override("separation", 0)
	var bar := PanelContainer.new()
	bar.theme_type_variation = &"Bar"
	var buttons := HBoxContainer.new()
	for button: Array in [["Card", "plus", "Add a card", _add_card], ["", "duplicate", "Duplicate the selected card", _duplicate_card],
			["", "trash", "Remove the selected card", _remove_card], [], ["Column", "plus", "Add a column", _ask.bind("New column", "", _add_column)],
			["", "rename", "Rename the selected column", _rename_selected_column], ["", "trash", "Remove the selected column", _remove_column]]:
		if button.is_empty():
			buttons.add_child(VSeparator.new())
			continue
		var widget := Button.new()
		widget.theme_type_variation = &"FlatButton"
		widget.text = button[0]
		widget.icon = EditorIcons.icon(button[1], Color.WHITE, 0.85)
		widget.tooltip_text = button[2]
		widget.pressed.connect(button[3])
		buttons.add_child(widget)
	bar.add_child(buttons)
	add_child(bar)
	var body := HBoxContainer.new()
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.hide_root = true
	grid.column_titles_visible = true
	grid.select_mode = Tree.SELECT_SINGLE
	grid.item_edited.connect(_on_cell_edited)
	grid.cell_selected.connect(_show_preview)
	body.add_child(grid)
	preview_spot.custom_minimum_size = CardView.SIZE * PREVIEW_SCALE + Vector2(20, 20)
	body.add_child(preview_spot)
	add_child(body)


func show_deck(csv_text: String, face: Dictionary) -> void:
	rows = DeckLoader.rows(csv_text)
	card_face = face
	_fill()


func _fill() -> void:
	grid.clear()
	if rows.is_empty():
		rows.append(PackedStringArray(["name"]))
	var headers := rows[0]
	grid.columns = headers.size()
	for column in headers.size():
		grid.set_column_title(column, headers[column])
		grid.set_column_expand(column, true)
	var root := grid.create_item()
	for row in range(1, rows.size()):
		var item := grid.create_item(root)
		for column in headers.size():
			item.set_text(column, rows[row][column] if column < rows[row].size() else "")
			item.set_editable(column, true)
	_show_preview()


func _row_of(item: TreeItem) -> int:
	return item.get_index() + 1 if item else -1


func _on_cell_edited() -> void:
	var item := grid.get_edited()
	var column := grid.get_edited_column()
	var row := rows[_row_of(item)]
	while row.size() < rows[0].size():
		row.append("")
	row[column] = item.get_text(column)
	rows[_row_of(item)] = row
	_send()


func _add_card() -> void:
	var blank := PackedStringArray()
	blank.resize(rows[0].size())
	rows.append(blank)
	_fill()
	_send()


func _duplicate_card() -> void:
	var row := _row_of(grid.get_selected())
	if row > 0:
		rows.insert(row + 1, rows[row].duplicate())
		_fill()
		_send()


func _remove_card() -> void:
	var row := _row_of(grid.get_selected())
	if row > 0:
		rows.remove_at(row)
		_fill()
		_send()


func _add_column(name: String) -> void:
	if name.is_empty() or name in rows[0]:
		return
	for row in rows.size():
		var cells := rows[row]
		cells.append(name if row == 0 else "")
		rows[row] = cells
	_fill()
	_send()


func _rename_selected_column() -> void:
	var column := grid.get_selected_column()
	if column >= 0:
		_ask("Rename column", rows[0][column], func(name: String) -> void:
			if name and name not in rows[0]:
				var headers := rows[0]
				headers[column] = name
				rows[0] = headers
				_fill()
				_send())


func _remove_column() -> void:
	var column := grid.get_selected_column()
	if column >= 0 and rows[0].size() > 1:
		for row in rows.size():
			var cells := rows[row]
			if column < cells.size():
				cells.remove_at(column)
			rows[row] = cells
		_fill()
		_send()


func _send() -> void:
	_show_preview()
	edited.emit(DeckLoader.to_csv(rows))


## The selected card (or the first) drawn with the game's card face.
func _show_preview() -> void:
	if preview:
		preview.queue_free()
		preview = null
	var row := _row_of(grid.get_selected())
	if row < 1:
		row = 1
	if row >= rows.size():
		return
	var data := {}
	for column in rows[0].size():
		data[rows[0][column]] = rows[row][column] if column < rows[row].size() else ""
	preview = CardView.new(Card.new(data), card_face)
	preview.scale = Vector2(PREVIEW_SCALE, PREVIEW_SCALE)
	preview.pivot_offset = Vector2.ZERO
	preview.position = Vector2(10, 10)
	preview.mouse_filter = Control.MOUSE_FILTER_IGNORE
	preview_spot.add_child(preview)


func _ask(title: String, start: String, then: Callable) -> void:
	var dialog := ConfirmationDialog.new()
	dialog.title = title
	var line := LineEdit.new()
	line.text = start
	dialog.add_child(line)
	dialog.register_text_enter(line)
	dialog.confirmed.connect(func() -> void: then.call(line.text.strip_edges()))
	add_child(dialog)
	dialog.popup_centered(Vector2i(320, 80))
	line.grab_focus()

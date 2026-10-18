extends Control
## Host or join a game, take seats, and (as host) fill empty seats with bots and start.

const TITLE_SCENE := "res://scenes/title.tscn"
const MAIN_SCENE := "res://scenes/main.tscn"
const OPEN_SEAT_COLOR := Color(1, 1, 1, 0.5)


func _ready() -> void:
	%Felt.color = TableView.FELT_COLOR
	%PlayerName.text = Net.my_name
	%HostPort.value = Net.DEFAULT_PORT
	%JoinPort.value = Net.DEFAULT_PORT
	for path in GameRules.game_paths():
		%GamePicker.add_item(GameRules.load_json(path).title)
		%GamePicker.set_item_metadata(%GamePicker.item_count - 1, path)
	%HostButton.pressed.connect(_host)
	%JoinButton.pressed.connect(_join)
	%GamePicker.item_selected.connect(func(index: int) -> void: Net.choose_game(%GamePicker.get_item_metadata(index)))
	%StartButton.pressed.connect(Net.start_match)
	%LeaveButton.pressed.connect(Net.leave)
	%BackButton.pressed.connect(func() -> void:
		Net.leave()
		get_tree().change_scene_to_file(TITLE_SCENE))
	Net.lobby_changed.connect(_refresh)
	Net.notice.connect(func(text: String) -> void: %Status.text = text)
	Net.match_started.connect(func() -> void: get_tree().change_scene_to_file(MAIN_SCENE))
	_refresh()


func _host() -> void:
	Net.my_name = %PlayerName.text.strip_edges()
	if Net.host(int(%HostPort.value)) == OK:
		Net.choose_game(%GamePicker.get_item_metadata(%GamePicker.selected))


func _join() -> void:
	Net.my_name = %PlayerName.text.strip_edges()
	Net.join(%Address.text.strip_edges(), int(%JoinPort.value))


func _refresh() -> void:
	%Connect.visible = not Net.online
	%PlayerName.editable = not Net.online
	%Room.visible = Net.online and Net.lobby.has("seats")
	if not %Room.visible:
		return
	%GamePicker.visible = Net.is_host()
	for index in %GamePicker.item_count:
		if %GamePicker.get_item_metadata(index) == Net.lobby.game:
			%GamePicker.select(index)
	%GameTitle.visible = not Net.is_host()
	%GameTitle.text = Net.lobby.title
	%Description.text = Net.lobby.description
	%StartButton.visible = Net.is_host()
	%StartButton.disabled = not Net.can_start()
	for child in %Seats.get_children():
		child.queue_free()
	for i in Net.lobby.seats.size():
		_add_seat(i + 1, Net.lobby.seats[i])


## One row per seat: its number, who's in it, a "Sit here" button, and (for the host) a Bot switch.
func _add_seat(seat: int, occupant: Dictionary) -> void:
	var number := Label.new()
	number.text = "Seat %d" % seat
	%Seats.add_child(number)
	var who := Label.new()
	who.custom_minimum_size.x = 260
	if occupant.bot:
		who.text = "Bot"
	elif occupant.peer:
		who.text = occupant.name + (" (you)" if occupant.peer == multiplayer.get_unique_id() else "")
	else:
		who.text = "open"
		who.add_theme_color_override("font_color", OPEN_SEAT_COLOR)
	%Seats.add_child(who)
	var sit := Button.new()
	sit.text = "Sit here"
	sit.disabled = occupant.bot or occupant.peer != 0
	sit.pressed.connect(Net.sit.bind(seat))
	%Seats.add_child(sit)
	var bot := CheckBox.new()
	bot.text = "Bot"
	bot.button_pressed = occupant.bot
	bot.disabled = not Net.is_host() or not Net.lobby.can_bot or occupant.peer != 0
	bot.toggled.connect(func(on: bool) -> void: Net.set_bot(seat, on))
	%Seats.add_child(bot)

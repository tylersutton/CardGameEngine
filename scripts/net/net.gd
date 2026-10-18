extends Node
## Online play (an autoload, so it lasts across scenes). In the lobby, people take seats and the host fills
## the rest with bots. In a match, every machine runs its own Game from the same rules, decks and seed; the
## host checks each input with Game.apply, numbers it and sends it to everyone, so all copies stay identical.
## Bots run only on the host. Each input comes with the host's state hash, so a copy that drifts is rebuilt.

signal lobby_changed
signal match_started
signal notice(text: String)
signal undo_asked(asker: String)
signal disconnected

const DEFAULT_PORT := 7777
const MAX_PEERS := 8
const HOST := 1
const NO_SEAT := 0

var online := false
var my_name := "Player"
var names: Dictionary = {}
## The chosen game and one {peer, bot, name} per seat; name stays after a disconnect so the player can rejoin.
var lobby: Dictionary = {}
var game: Game
var game_names: Array = []
var match_package: Dictionary = {}
var waiting_inputs: Array = []
var table_ready := false
var catching_up := false
var undo_request: Dictionary = {}


func _ready() -> void:
	multiplayer.peer_disconnected.connect(_on_peer_disconnected)
	multiplayer.connected_to_server.connect(func() -> void: _hello.rpc_id(HOST, my_name))
	multiplayer.connection_failed.connect(func() -> void: _lose_connection("Couldn't reach the host"))
	multiplayer.server_disconnected.connect(func() -> void: _lose_connection("The host left"))


func is_host() -> bool:
	return online and multiplayer.is_server()


func host(port: int) -> Error:
	var peer := ENetMultiplayerPeer.new()
	var error := peer.create_server(port, MAX_PEERS)
	if error:
		notice.emit("Couldn't host on port %d: %s" % [port, error_string(error)])
		return error
	_go_online(peer)
	names = {HOST: my_name}
	notice.emit("Hosting on port %d. Others join with this computer's address." % port)
	return OK


func join(address: String, port: int) -> Error:
	var peer := ENetMultiplayerPeer.new()
	var error := peer.create_client(address, port)
	if error:
		notice.emit("Couldn't connect: " + error_string(error))
		return error
	_go_online(peer)
	notice.emit("Connecting to %s:%d..." % [address, port])
	return OK


func leave() -> void:
	if multiplayer.multiplayer_peer:
		multiplayer.multiplayer_peer.close()
	multiplayer.multiplayer_peer = null
	online = false
	names = {}
	lobby = {}
	game = null
	table_ready = false
	lobby_changed.emit()


func _go_online(peer: ENetMultiplayerPeer) -> void:
	multiplayer.multiplayer_peer = peer
	online = true
	lobby = {}


func _lose_connection(message: String) -> void:
	leave()
	notice.emit(message)
	disconnected.emit()


# --- Lobby -----------------------------------------------------------------------------------------------

func choose_game(path: String) -> void:
	var rules := GameRules.load_json(path)
	var seats := []
	for seat in rules.players:
		seats.append({peer = 0, bot = false, name = ""})
	lobby = {game = path, title = rules.title, description = rules.description, can_bot = rules.bot.has("prefer"), seats = seats}
	_share_lobby()


func my_seat() -> int:
	for i in lobby.get("seats", []).size():
		if lobby.seats[i].peer == multiplayer.get_unique_id():
			return i + 1
	return NO_SEAT


func sit(seat: int) -> void:
	if is_host():
		_sit(HOST, seat)
	else:
		_request_seat.rpc_id(HOST, seat)


func set_bot(seat: int, bot: bool) -> void:
	if is_host() and lobby.can_bot:
		lobby.seats[seat - 1] = {peer = 0, bot = bot, name = ""}
		_share_lobby()


func can_start() -> bool:
	return is_host() and lobby.has("seats") and lobby.seats.all(func(seat: Dictionary) -> bool: return seat.bot or seat.peer)


@rpc("any_peer", "reliable")
func _hello(player_name: String) -> void:
	var peer := multiplayer.get_remote_sender_id()
	while player_name in names.values():
		player_name += "+"
	names[peer] = player_name
	if game:
		_take_back_seat(peer, player_name)
	_share_lobby()
	if game:
		_begin_match.rpc_id(peer, match_package, game.inputs)


@rpc("any_peer", "reliable")
func _request_seat(seat: int) -> void:
	_sit(multiplayer.get_remote_sender_id(), seat)


func _sit(peer: int, seat: int) -> void:
	if game or seat < 1 or seat > lobby.seats.size() or lobby.seats[seat - 1].peer or lobby.seats[seat - 1].bot:
		return
	for each: Dictionary in lobby.seats:
		if each.peer == peer:
			each.merge({peer = 0, name = ""}, true)
	lobby.seats[seat - 1] = {peer = peer, bot = false, name = names[peer]}
	_share_lobby()


func _share_lobby() -> void:
	_receive_lobby.rpc(lobby, names)


@rpc("authority", "call_local", "reliable")
func _receive_lobby(new_lobby: Dictionary, new_names: Dictionary) -> void:
	lobby = new_lobby
	names = new_names
	if game:
		game.rules.player_names = _seat_names()
		game.state_changed.emit()
	lobby_changed.emit()


func _on_peer_disconnected(peer: int) -> void:
	if not is_host():
		return
	notice.emit("%s left" % names.get(peer, "Someone"))
	names.erase(peer)
	for seat: Dictionary in lobby.get("seats", []):
		if seat.peer == peer and not game:
			seat.merge({peer = 0, name = ""}, true)
	if undo_request.get("waiting", []).has(peer):
		_undo_answered(peer, false)
	_share_lobby()


## Seat tags show each person's name, with "(away)" while they're disconnected; bots keep the game's names.
func _seat_names() -> Array:
	var seat_names := []
	for i in lobby.seats.size():
		var seat: Dictionary = lobby.seats[i]
		if seat.bot:
			seat_names.append(game_names[i] if i < game_names.size() else "Player %d" % (i + 1))
		else:
			seat_names.append(seat.name + ("" if seat.peer in names else " (away)"))
	return seat_names


# --- Match -----------------------------------------------------------------------------------------------

func start_match() -> void:
	if not can_start():
		return
	var bots := []
	for i in lobby.seats.size():
		if lobby.seats[i].bot:
			bots.append(i + 1)
	_begin_match.rpc({path = lobby.game, files = _match_files(lobby.game), seed = randi(), bots = bots}, [])


static func _match_files(path: String) -> Dictionary:
	var files := {path: FileAccess.get_file_as_string(path)}
	for zone: Dictionary in GameRules.load_json(path).zones.values():
		var deck: Variant = zone.get("cards")
		if deck is String and FileAccess.file_exists(deck):
			files[deck] = FileAccess.get_file_as_string(deck)
	return files


## Builds this machine's copy of the match. The table calls catch_up() once it's showing it.
@rpc("authority", "call_local", "reliable")
func _begin_match(package: Dictionary, inputs_so_far: Array) -> void:
	var new_game := Game.new(package.path, int(package.seed), package.files)
	if new_game.errors:
		notice.emit("Couldn't load the host's game: " + "; ".join(new_game.errors))
		return
	if package.bots:
		new_game.rules.bot.players = package.bots
	game_names = new_game.rules.player_names.duplicate()
	new_game.rules.player_names = _seat_names()
	var seat := my_seat()
	new_game.local_seat = seat if seat else new_game.rules.players + 1
	match_package = package
	game = new_game
	waiting_inputs = inputs_so_far.map(func(input: Dictionary) -> Array: return [input, null])
	table_ready = false
	catching_up = false
	if is_host():
		game.input_applied.connect(_send_input)
	match_started.emit()


func catch_up() -> void:
	table_ready = true
	for entry: Array in waiting_inputs:
		_apply_from_host(entry[0], entry[1])
	waiting_inputs.clear()


## Someone joining a match in progress gets back the seat they left (same name), or watches.
func _take_back_seat(peer: int, player_name: String) -> void:
	for seat: Dictionary in lobby.seats:
		if not seat.bot and seat.name == player_name and seat.peer not in names:
			seat.peer = peer
			notice.emit(player_name + " rejoined")
			return


## The table's way in: the host applies its own inputs as its seat; everyone else asks the host.
## A drag's answer is a guess (is it allowed here?) so the card doesn't snap back while the host answers.
func submit(input: Dictionary) -> bool:
	if input.type == "undo":
		request_undo()
		return false
	if is_host():
		var sent := input.duplicate()
		sent.player = game.local_seat
		return game.apply(sent)
	_request_input.rpc_id(HOST, input)
	return input.type != "drag" or (game.may_act(game.local_seat, input) and game.can_move(game.cards[input.card], game.zones[input.to]))


@rpc("any_peer", "reliable")
func _request_input(input: Dictionary) -> void:
	var seat := _seat_of(multiplayer.get_remote_sender_id())
	if not game or not seat:
		return
	var sent := input.duplicate()
	sent.player = seat
	game.apply(sent)


func _send_input(input: Dictionary) -> void:
	_receive_input.rpc(input, game.state_hash())


@rpc("authority", "reliable")
func _receive_input(input: Dictionary, host_hash: int) -> void:
	if catching_up:
		return
	if table_ready:
		_apply_from_host(input, host_hash)
	else:
		waiting_inputs.append([input, host_hash])


func _apply_from_host(input: Dictionary, host_hash: Variant) -> void:
	game.apply(input)
	if host_hash != null and game.state_hash() != host_hash:
		notice.emit("Out of step with the host; catching up")
		catching_up = true
		_request_resync.rpc_id(HOST)


@rpc("any_peer", "reliable")
func _request_resync() -> void:
	_begin_match.rpc_id(multiplayer.get_remote_sender_id(), match_package, game.inputs)


func _seat_of(peer: int) -> int:
	for i in lobby.seats.size():
		if lobby.seats[i].peer == peer:
			return i + 1
	return NO_SEAT


# --- Undo: everyone else at the table has to agree -------------------------------------------------------

func request_undo() -> void:
	if is_host():
		_undo_requested(HOST)
	else:
		_request_undo.rpc_id(HOST)


func answer_undo(allow: bool) -> void:
	if is_host():
		_undo_answered(HOST, allow)
	else:
		_answer_undo.rpc_id(HOST, allow)


@rpc("any_peer", "reliable")
func _request_undo() -> void:
	_undo_requested(multiplayer.get_remote_sender_id())


@rpc("any_peer", "reliable")
func _answer_undo(allow: bool) -> void:
	_undo_answered(multiplayer.get_remote_sender_id(), allow)


func _undo_requested(peer: int) -> void:
	var seat := _seat_of(peer)
	if not seat or undo_request:
		return
	var others: Array = lobby.seats.map(func(each: Dictionary) -> int: return each.peer) \
		.filter(func(other: int) -> bool: return other and other != peer and other in names)
	if others.is_empty():
		game.apply({type = "undo", player = seat})
		return
	undo_request = {seat = seat, peer = peer, waiting = others}
	for other: int in others:
		if other == HOST:
			undo_asked.emit(names[peer])
		else:
			_ask_undo.rpc_id(other, names[peer])


@rpc("authority", "reliable")
func _ask_undo(asker: String) -> void:
	undo_asked.emit(asker)


func _undo_answered(peer: int, allow: bool) -> void:
	if not undo_request.get("waiting", []).has(peer):
		return
	if not allow:
		_tell(undo_request.peer, "%s didn't allow the undo" % names.get(peer, "Someone"))
		undo_request = {}
		return
	undo_request.waiting.erase(peer)
	if undo_request.waiting.is_empty():
		game.apply({type = "undo", player = undo_request.seat})
		undo_request = {}


func _tell(peer: int, text: String) -> void:
	if peer == HOST:
		notice.emit(text)
	elif peer in names:
		_receive_notice.rpc_id(peer, text)


@rpc("authority", "reliable")
func _receive_notice(text: String) -> void:
	notice.emit(text)

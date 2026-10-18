extends Node
## Two processes play a hand of Hearts over the network (two people, two bots on the host), then compare.
## The guest asks for an undo partway (the host allows it), drops out and rejoins by name, and finally
## scribbles on its own copy to check it notices it's out of step and catches up.
## Run both, then check the two printed hashes match:
##   godot --headless res://tests/net_test.tscn -- host
##   godot --headless res://tests/net_test.tscn -- guest

const PORT := 7790
const GAME := "res://games/hearts.json"
const TIME_LIMIT := 240.0
const UNDO_AT := 20
const REJOIN_AT := 40
const CORRUPT_AT := 60

var hosting := OS.get_cmdline_user_args().has("host")
var bot: Bot
var chooser := RandomNumberGenerator.new()
var waiting_for := -1
var asked_undo := false
var rejoined := false
var corrupted := false


func _ready() -> void:
	chooser.seed = 7 if hosting else 8
	Net.match_started.connect(_on_match_started)
	Net.notice.connect(func(text: String) -> void: print("  [%s] %s" % [_role(), text]))
	Net.undo_asked.connect(func(asker: String) -> void:
		print("  [host] %s asks to undo: allowing" % asker)
		Net.answer_undo(true))
	get_tree().create_timer(TIME_LIMIT).timeout.connect(func() -> void: _finish("timed out"))
	if hosting:
		Net.my_name = "Host"
		Net.host(PORT)
		Net.choose_game(GAME)
		Net.sit(3)
		Net.set_bot(2, true)
		Net.set_bot(4, true)
		Net.lobby_changed.connect(func() -> void:
			if Net.can_start() and not Net.game:
				Net.start_match())
	else:
		Net.my_name = "Guest"
		Net.lobby_changed.connect(func() -> void:
			if Net.lobby.has("seats") and not Net.game and Net.my_seat() == 0:
				Net.sit(1))
		Net.join("127.0.0.1", PORT)


func _role() -> String:
	return "host" if hosting else "guest"


func _on_match_started() -> void:
	Net.game.start()
	Net.catch_up()
	bot = Bot.new(Net.game)
	waiting_for = -1
	print("  [%s] match started as seat %d with %d inputs so far" % [_role(), Net.game.local_seat, Net.game.inputs.size()])
	var other_person := 1 if hosting else 3
	var other_card: Card = Net.game.zones["hand_%d" % other_person].cards[0] if Net.game.zones["hand_%d" % other_person].cards else null
	if Net.game.is_bot(Net.game.local_seat) or Net.game.is_bot(other_person):
		print("  [%s] PROBLEM: a person's seat is played by a bot" % _role())
	if other_card and (Net.game.shows_face(other_card) or Net.game.may_handle(other_card)):
		print("  [%s] PROBLEM: can see or pick up the other person's cards" % _role())


func _process(_delta: float) -> void:
	var game := Net.game
	if not game or not Net.table_ready:
		return
	if game.variables.phase == "hand over":
		_finish("hand over")
		return
	if game.inputs.size() < waiting_for:
		return
	if not hosting and not asked_undo and game.inputs.size() >= UNDO_AT:
		asked_undo = true
		print("  [guest] asking to undo at %d inputs" % game.inputs.size())
		Net.request_undo()
		waiting_for = game.inputs.size() + 1
		return
	if not hosting and not rejoined and game.inputs.size() >= REJOIN_AT:
		rejoined = true
		print("  [guest] dropping out at %d inputs, then rejoining" % game.inputs.size())
		Net.leave()
		await get_tree().create_timer(1.0).timeout
		Net.join("127.0.0.1", PORT)
		return
	if not hosting and not corrupted and game.inputs.size() >= CORRUPT_AT:
		corrupted = true
		print("  [guest] scribbling on its own copy at %d inputs" % game.inputs.size())
		game.variables.tricks += 5
	if hosting and game.can_perform("Collect trick"):
		Net.submit({type = "perform", action = "Collect trick"})
	elif hosting and game.is_bot_turn():
		bot.take_input()
	elif game.is_local_turn():
		var moves := game.legal_moves()
		if moves.is_empty():
			return
		var applied_before := game.inputs.size()
		for input: Dictionary in moves[chooser.randi_range(0, moves.size() - 1)]:
			Net.submit(input)
		if not hosting:
			waiting_for = applied_before + 1


func _finish(reason: String) -> void:
	var game := Net.game
	print("%s: %s after %d inputs, state %d" % [_role().to_upper(), reason, game.inputs.size() if game else 0, game.state_hash() if game else 0])
	if game and reason == "timed out":
		print("  stuck: phase %s, player %d, choosing '%s', asked %s, local %s, waiting_for %d, history %d" % [game.variables.phase, game.current_player(), game.choosing, game.asked, game.is_local_turn(), waiting_for, game.history.size()])
		print("  last inputs: ", game.inputs.slice(-6))
	set_process(false)
	await get_tree().create_timer(3.0 if hosting else 0.5).timeout
	get_tree().quit()

extends SceneTree
## Loads every game in games/, then plays Gin, Hearts and Color Match with bots and checks Solitaire's
## auto-finish and undo. Run after rule or engine changes: godot --headless -s tests/regression_test.gd

var problems := []


func _init() -> void:
	for path in GameRules.game_paths():
		var game := Game.new(path)
		game.start()
		check(game.errors.is_empty(), "%s loads: %s" % [path.get_file(), game.errors])
	gin()
	hearts()
	color_match()
	solitaire()
	card_effects()
	print("REGRESSION: ", "all passed" if problems.is_empty() else "FAILED %s" % problems)
	quit(0 if problems.is_empty() else 1)


func check(ok: bool, what: String) -> void:
	if not ok:
		problems.append(what)


func watch(game: Game) -> void:
	game.failed.connect(func(text: String) -> void: problems.append("failed: " + text))


func gin() -> void:
	var game := Game.new("res://games/gin_rummy.json")
	watch(game)
	game.start()
	var bot := Bot.new(game)
	for turn in 300:
		if game.variables.phase in ["over", "match over"]:
			break
		if game.current_player() == 1:
			game.apply_move(game.legal_moves().pick_random())
		else:
			while bot.take_input():
				pass
	check(game.variables.phase in ["over", "match over"], "gin hand ends (%s)" % game.variables.phase)


func hearts() -> void:
	var game := Game.new("res://games/hearts.json")
	watch(game)
	game.start()
	var bot := Bot.new(game)
	for step in 400:
		if game.variables.phase == "hand over":
			break
		if game.variables.phase == "trick done":
			game.perform("Collect trick")
		elif game.is_bot_turn():
			bot.take_input()
		else:
			game.apply_move(game.legal_moves().pick_random())
	var total := 0.0
	for player in range(1, 5):
		total += game.variables["score_%d" % player]
	check(game.variables.tricks == 13 and total == 26, "hearts hand: %d tricks, %d points" % [game.variables.tricks, total])


func color_match() -> void:
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://games/color_match.json"))
	data.bot = {"players": [1, 2], "prefer": "count(opponent_hand) - count(hand)"}
	var file := FileAccess.open("user://color_bots.json", FileAccess.WRITE)
	file.store_string(JSON.stringify(data))
	file.close()
	var game := Game.new("user://color_bots.json")
	watch(game)
	game.start()
	var bot := Bot.new(game)
	for step in 600:
		if game.variables.winner != null:
			break
		bot.take_input()
	check(game.variables.winner != null, "color match bots finish")


func solitaire() -> void:
	var game := Game.new("res://games/solitaire.json")
	watch(game)
	game.start()
	var z := game.zones
	var by_name := {}
	for zone in z.values():
		for card in zone.cards:
			by_name[card.title] = card
	for zone in z.values():
		zone.cards.clear()
	var ranks := ["K", "Q", "J", "10", "9", "8", "7", "6", "5", "4", "3", "2", "A"]
	var pairs := [["♠", "♥"], ["♥", "♠"], ["♣", "♦"], ["♦", "♣"]]
	for column in 4:
		for i in 13:
			var card: Card = by_name[ranks[i] + pairs[column][i % 2]]
			z["tableau_%d" % (column + 1)].add(card)
			card.face_up = true
	game.perform("Auto-finish")
	check(game.variables.result == "You win!", "solitaire auto-finish wins")
	game.undo()
	check(Actions.cards_in(game.zone_groups.foundation).is_empty(), "solitaire undo")


## run_effect replays a card's effect (Dominion's Throne Room): a Smithy's should draw 3.
func card_effects() -> void:
	var game := Game.new("res://games/dominion.json")
	watch(game)
	game.start()
	var smithy: Card = game.cards.filter(func(card: Card) -> bool: return card.title == "Smithy")[0]
	var hand_before: int = game.zones.hand_1.cards.size()
	Actions.new(game).run_effect(smithy, "play")
	check(game.zones.hand_1.cards.size() == hand_before + 3, "run_effect: Smithy drew %d" % (game.zones.hand_1.cards.size() - hand_before))

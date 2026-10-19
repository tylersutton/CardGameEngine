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
	clicked_copy()
	print("REGRESSION: ", "all passed" if problems.is_empty() else "FAILED %s" % problems)
	quit(0 if problems.is_empty() else 1)


func check(ok: bool, what: String) -> void:
	if not ok:
		problems.append(what)


func watch(game: Game) -> void:
	game.failed.connect(func(text: String) -> void: problems.append("failed: " + text))


func gin() -> void:
	var game := Game.new("res://games/gin_rummy.jsonc")
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
	var game := Game.new("res://games/hearts.jsonc")
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
	check(game.variables.tricks == 13 and total in [26.0, 78.0], "hearts hand: %d tricks, %d points (26, or 78 when someone shoots the moon)" % [game.variables.tricks, total])


func color_match() -> void:
	var data: Dictionary = GameRules.read_file("res://games/color_match.jsonc")
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
	var game := Game.new("res://games/solitaire.jsonc")
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
	var game := Game.new("res://games/dominion.jsonc")
	watch(game)
	game.start()
	var smithy: Card = game.cards.filter(func(card: Card) -> bool: return card.title == "Smithy")[0]
	var hand_before: int = game.zones.hand_1.cards.size()
	Actions.new(game).run_effect(smithy, "play")
	check(game.zones.hand_1.cards.size() == hand_before + 3, "run_effect: Smithy drew %d" % (game.zones.hand_1.cards.size() - hand_before))


## Clicking one of several copies tells the steps which one it was, even while it's empty.
func clicked_copy() -> void:
	var rules := '{ "zones": { "deck": { "layout": "pile", "cards": "res://decks/standard_deck.csv" },'		+ ' "pile": { "layout": "pile", "copies": 2, "on_click": "move(top(deck), clicked_zone)" } } }'
	var game := Game.new("res://clicked_copy_test.jsonc", 1, {"res://clicked_copy_test.jsonc": rules})
	watch(game)
	game.start()
	check(game.errors.is_empty(), "clicked_copy loads: %s" % [game.errors])
	game.apply({type = "click_empty", zone = "pile_2"})
	game.apply({type = "click", card = game.cards.find(game.zones.pile_2.cards[0])})
	check(game.zones.pile_1.cards.is_empty() and game.zones.pile_2.cards.size() == 2, "clicked_zone: piles hold %d and %d" 		% [game.zones.pile_1.cards.size(), game.zones.pile_2.cards.size()])

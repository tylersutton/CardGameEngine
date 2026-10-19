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
	remodel()
	sea_salt()
	sea_salt_scoring()
	clicked_copy()
	pick_and_up_to()
	print("REGRESSION: ", "all passed" if problems.is_empty() else "FAILED %s" % [problems])
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
		if "Next hand" in game.offered or game.over:
			break
		if game.current_player() == 1:
			game.apply_move(game.legal_moves().pick_random())
		else:
			while bot.take_input():
				pass
	check("Next hand" in game.offered or game.over, "gin hand ends (offered: %s)" % [game.offered])


func hearts() -> void:
	var game := Game.new("res://games/hearts.jsonc")
	watch(game)
	game.start()
	var bot := Bot.new(game)
	for step in 400:
		if "Next hand" in game.offered or game.over:
			break
		if "Collect trick" in game.offered:
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


## Remodel chooses twice in a row: a card to trash, then one to gain costing up to 2 more.
func remodel() -> void:
	var game := Game.new("res://games/dominion.jsonc", 4)
	watch(game)
	game.start()
	var named := func(title: String) -> Card: return game.cards.filter(func(card: Card) -> bool: return card.title == title and game.zone_of(card) != game.zones.hand_1)[0]
	var remodel_card: Card = named.call("Remodel")
	var estate: Card = named.call("Estate")
	game.move(remodel_card, game.zones.hand_1)
	game.move(estate, game.zones.hand_1)
	game.apply({type = "click", card = remodel_card.id})
	check(game.choosing.begins_with("Remodel: trash"), "remodel asks for a card to trash: '%s'" % game.choosing)
	game.apply({type = "click", card = estate.id})
	check(estate in game.zones.trash.cards and game.choosing.begins_with("Remodel: gain"), "the estate is trashed, then a gain is asked: '%s'" % game.choosing)
	var silver: Card = game.choices.filter(func(card: Card) -> bool: return card.title == "Silver")[0]
	check(game.choices.all(func(card: Card) -> bool: return int(card.data.cost) <= 4), "only cards costing up to 4 are offered")
	game.apply({type = "click", card = silver.id})
	check(silver in game.zones.discard_1.cards and game.choosing == "", "the silver is gained")


## Sea Salt: draw two and keep one, discard the other onto a chosen pile, then two crabs take a card from a pile.
func sea_salt() -> void:
	var game := Game.new("res://games/sea_salt_+_paper.jsonc", 2)
	watch(game)
	game.start()
	game.apply({type = "click", card = game.zones.deck.cards.back().id})
	check(game.choosing == "Keep one card" and game.choices.size() == 2, "drawing offers the two cards: '%s'" % game.choosing)
	var kept: Card = game.choices[0]
	game.apply({type = "click", card = kept.id})
	check(kept in game.zones.hand_1.cards and game.choosing == "Discard the other card onto a pile", "the kept card is in hand, then a pile is asked: '%s'" % game.choosing)
	game.apply({type = "choose", option = "discard_piles_2"})
	check(game.zones.discard_piles_2.cards.size() == 2 and game.offered == ["Play duo", "End turn", "Stop", "Last chance"], "the other card is on pile 2, then duos are offered: %s" % [game.offered])
	var crabs := game.zones.deck.cards.filter(func(card: Card) -> bool: return card.data.Kind == "Crab").slice(0, 2)
	for crab: Card in crabs:
		game.move(crab, game.zones.hand_1)
	game.apply({type = "perform", action = "Play duo"})
	for crab: Card in crabs:
		game.apply({type = "click", card = crab.id})
	check(game.choosing == "Crabs: look through which pile?", "two crabs ask for a pile: '%s'" % game.choosing)
	game.apply({type = "choose", option = "discard_piles_2"})
	check(game.zones.browsing.cards.size() == 2, "the pile is spread out to look through")
	var taken: Card = game.zones.browsing.cards[0]
	game.apply({type = "click", card = taken.id})
	check(taken in game.zones.hand_1.cards and game.zones.discard_piles_2.cards.size() == 1 and game.zones.browsing.is_empty(), "one card is taken, the rest go back")


## Three shells (4), two crabs (a duo, 1) and a mermaid with two cards of each color (2) make 7 points.
func sea_salt_scoring() -> void:
	var game := Game.new("res://games/sea_salt_+_paper.jsonc", 3)
	watch(game)
	game.start()
	for kind in ["Shell", "Shell", "Shell", "Crab", "Crab", "Mermaid", "Octopus"]:
		var card: Card = game.cards.filter(func(each: Card) -> bool: return each.data.Kind == kind and game.zone_of(each) != game.zones.hand_1)[0]
		game.move(card, game.zones.hand_1)
	check(game.evaluate("my_points") == 7 and game.evaluate("collection_points(hand)") == 4, "sea salt scoring: %s points" % game.evaluate("my_points"))


## Clicking one of several copies tells the steps which one it was, even while it's empty.
func clicked_copy() -> void:
	var rules := '{ "zones": { "deck": { "layout": "pile", "cards": "res://decks/standard_deck.csv" },' \
		+ ' "pile": { "layout": "pile", "copies": 2 } },' \
		+ ' "actions": { "Deal here": { "click": "pile", "steps": "move(top(deck), clicked_zone)" } } }'
	var game := Game.new("res://clicked_copy_test.jsonc", 1, {"res://clicked_copy_test.jsonc": rules})
	watch(game)
	game.start()
	check(game.errors.is_empty(), "clicked_copy loads: %s" % [game.errors])
	game.apply({type = "click_empty", zone = "pile_2"})
	game.apply({type = "click", card = game.cards.find(game.zones.pile_2.cards[0])})
	check(game.zones.pile_1.cards.is_empty() and game.zones.pile_2.cards.size() == 2, "clicked_zone: piles hold %d and %d" \
		% [game.zones.pile_1.cards.size(), game.zones.pile_2.cards.size()])


func pick_and_up_to() -> void:
	var rules := '{ "zones": { "hand": { "layout": "fan" } },' \
		+ ' "actions": { "Pass": { "choose": "hand", "pick": 2, "up_to": 2, "steps": [] } } }'
	var game := Game.new("res://pick_test.jsonc", 1, {"res://pick_test.jsonc": rules})
	check(game.errors.any(func(error: String) -> bool: return error.contains("not both")), "pick with up_to is a load error: %s" % [game.errors])

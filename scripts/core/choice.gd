class_name Choice
extends RefCounted
## The choice being made, if any: a button's "choose" or a choose(...) step that waits for its answer. And the
## asked-for choices (ask, ask_all, ask_others) waiting their turn, each made once the one before is finished.

const CHOOSE_PROMPT := "Choose"

## Held weakly: the game holds its choice, and a reference each way would keep both alive forever.
var game: Game:
	get: return game_reference.get_ref()
var game_reference: WeakRef
## What the choice is called (an action's name, or choose(...)'s prompt; "" while nothing is being chosen), the
## action whose steps run once it's made ("" for a choose(...) step, which carries on instead), what can be
## picked, how many (exactly, or up to so many), what's picked so far, and whether it must be made (asked for:
## clicking elsewhere doesn't cancel it, and nothing else happens until it's made).
var title := ""
var action := ""
var options: Array = []
var pick := 1
var exactly := true
var selected: Array[Card] = []
var must := false
## Whose turn it goes back to once the choice is made (asked of another player), or 0.
var return_player := 0
var asked: Array[Dictionary] = []
var performing_asked := false


func _init(game_to_serve: Game) -> void:
	game_reference = weakref(game_to_serve)


func is_open() -> bool:
	return title != ""


## A choice a rule asked for (e.g. "discard down to 3") must be made before anything else happens.
func must_be_made() -> bool:
	return is_open() and must


func cards_offered() -> bool:
	return is_open() and options.all(func(option: Variant) -> bool: return option is Card)


## What a choice offers: the cards of a zone (or of a mix of zones and cards), the zones of a list of zones (pick
## a pile), or the items of a list of values (pick a color).
static func options_from(value: Variant) -> Array:
	var offered := []
	if value is Array and value.size() > 0 and value.all(func(option: Variant) -> bool: return option is Zone):
		offered.assign(value)
	elif value is Array and value.any(func(option: Variant) -> bool: return option is not Card and option is not Zone and option is not Array):
		offered.assign(value)
	else:
		# Untyped, so a zone can be looked for among cards (a click on a pile while cards are offered).
		offered.assign(Actions.cards_in(value))
	return offered


## How an option is written on its button: a zone by its label, or its name when the label doesn't tell it apart.
func option_text(option: Variant) -> String:
	if option is Zone:
		var same_label := options.filter(func(other: Variant) -> bool: return other is Zone and other.label == option.label)
		return option.label if option.label and same_label.size() == 1 else option.name
	return Game.format_value(option)


## A button with "choose" was pressed (or asked for): its options are worked out and the choice opens.
func start_for_action(action_name: String) -> void:
	var button: Dictionary = game.rules.actions[action_name]
	var offered := options_from(game.evaluate(button.choose, null))
	var rule := _pick_rule(button, offered.size())
	if offered.is_empty() or offered.size() < rule[0]:
		game.log_line("  nothing to choose")
		return
	open(action_name, action_name, offered, rule[0], rule[1], performing_asked)


## A choose(...) step is waiting: the current player picks, and must (nothing else happens until they do).
## The step's answer is a single option, or for several (exactly or up to so many) a list.
func open_inline(request: Dictionary) -> void:
	open(request.prompt if request.prompt else CHOOSE_PROMPT, "", request.options, request.amount, request.exact, true)


func open(choice_title: String, action_name: String, choice_options: Array, how_many: int, only_exactly: bool, must_make: bool) -> void:
	title = choice_title
	action = action_name
	options = choice_options
	pick = how_many
	exactly = only_exactly
	selected = []
	must = must_make
	var chooser := game.player_name(game.current_player())
	if not cards_offered():
		game.log_line("  choose one: " + ", ".join(options.map(Game.format_value)))
	elif exactly:
		game.log_line("  %s: choose %d (click the highlighted cards%s)" % [chooser, pick, "" if must else "; click anything else to cancel"])
	else:
		game.log_line("  %s: choose up to %d (click the highlighted cards, then Done)" % [chooser, pick])


## A highlighted card clicked: chosen at once when it's the only one to pick, otherwise (un)selected, and the
## choice made once exactly enough are selected.
func select(card: Card) -> void:
	if exactly and pick == 1:
		complete(card)
	elif card in selected:
		selected.erase(card)
	elif selected.size() < pick:
		selected.append(card)
		if exactly and selected.size() == pick:
			complete(selected.duplicate())


## Runs the action's steps with the choice, or carries on the choose(...) step that was waiting for it. The turn
## goes back to whoever had it once those steps are done (or at the end of a choice they wait for in turn).
func complete(made: Variant) -> void:
	var action_name := action
	if not allows(action_name, made):
		game.log_line("[not allowed] %s: requires %s" % [game.describe(made), game.rules.actions[action_name].allow])
		return
	var back_to := return_player
	game.log_line("[%s] chose %s" % [title, game.describe(made)])
	clear()
	if action_name and Plays.is_card_action(game.rules.actions[action_name]):
		game.play_card_action(action_name, made)
	elif action_name:
		game.plays.accept_offer(action_name)
		game.with_choice(made, func() -> void:
			game.run(game.rules.actions[action_name].get("steps", []), made if made is Card else null))
	else:
		game.runner.answer(made)
	if back_to and is_open():
		return_player = back_to
	elif back_to:
		game.set_variable(Game.PLAYER_VARIABLE, back_to)


## "allow" refuses a choice as a whole, e.g. cards that aren't all the same rank.
func allows(action_name: String, made: Variant) -> bool:
	var allow: String = game.rules.actions.get(action_name, {}).get("allow", "")
	if allow.is_empty():
		return true
	return game.with_choice(made, func() -> bool: return game.holds(allow, made if made is Card else null))


func cancel() -> void:
	if is_open() and not must:
		game.log_line("[cancelled] " + title)
		clear()


func clear() -> void:
	title = ""
	action = ""
	options = []
	selected = []
	must = false
	return_player = 0
	game.state_changed.emit()


func ask(action_name: String, player := 0) -> void:
	if action_name in game.rules.actions:
		asked.append({action = action_name, player = player})
	else:
		game.report_failure("No action named \"%s\" to ask for" % action_name)


## Everyone makes the same choice, people first so nobody waits on the bots, then then_trigger runs.
## Choices go somewhere hidden (like a face-down zone), so nobody sees the others' until the trigger reveals them.
func ask_all(action_name: String, then_trigger: String) -> void:
	if then_trigger and then_trigger not in game.rules.triggers:
		game.report_failure("No trigger named \"%s\" in \"triggers\"" % then_trigger)
		return
	var players := range(1, game.rules.players + 1)
	for player in players.filter(func(each: int) -> bool: return not game.is_bot(each)) + players.filter(game.is_bot):
		ask(action_name, player)
	if then_trigger:
		asked.append({trigger = then_trigger})


## A response window: each other player in turn order is asked, then then_trigger runs. A response that opens
## a new window (a "Nope" to a "Nope") calls off the rest of this one, so the trigger runs once, when nobody responds.
func ask_others(action_name: String, then_trigger: String) -> void:
	if then_trigger and then_trigger not in game.rules.triggers:
		game.report_failure("No trigger named \"%s\" in \"triggers\"" % then_trigger)
		return
	asked.assign(asked.filter(func(request: Dictionary) -> bool: return not request.get("window", false)))
	for seat in range(1, game.rules.players):
		asked.append({action = action_name, player = (game.current_player() - 1 + seat) % game.rules.players + 1, window = true})
	if then_trigger:
		asked.append({trigger = then_trigger, window = true})


## Asked-for actions run one at a time, each once the previous choice is finished.
func perform_asked() -> void:
	if performing_asked:
		return
	performing_asked = true
	while not game.runner.running and asked and not is_open():
		var request: Dictionary = asked.pop_front()
		if request.has("trigger"):
			game.trigger(request.trigger)
			continue
		var turn := game.current_player()
		var history_size := game.history.size()
		if request.player and request.player != turn:
			game.set_variable(Game.PLAYER_VARIABLE, request.player)
		game.perform(request.action)
		game.history.resize(history_size)
		if is_open():
			return_player = turn if game.current_player() != turn else 0
		elif game.current_player() != turn:
			game.set_variable(Game.PLAYER_VARIABLE, turn)
	performing_asked = false
	game.runner.drive()


## Every way to make the open choice, for bots: each as the inputs that make it.
func moves() -> Array[Array]:
	return _moves(action, options, pick, exactly)


## Every way to make a button's choice once pressed (its options worked out now), for bots.
func moves_for_action(action_name: String) -> Array[Array]:
	var button: Dictionary = game.rules.actions[action_name]
	var offered := options_from(game.evaluate(button.choose, null))
	var rule := _pick_rule(button, offered.size())
	return _moves(action_name, offered, rule[0], rule[1])


## The choice's state as plain data, and back (for undo, bots trying moves out and the state hash).
func state() -> Dictionary:
	return {title = title, action = action, options = options.duplicate(), pick = pick, exactly = exactly, selected = selected.duplicate(),
		must = must, return_player = return_player, asked = asked.duplicate(true)}


func restore(saved: Dictionary) -> void:
	title = saved.title
	action = saved.action
	options = saved.options.duplicate()
	pick = saved.pick
	exactly = saved.exactly
	selected.assign(saved.selected)
	must = saved.must
	return_player = saved.return_player
	asked.assign(saved.asked.duplicate(true))


## How many to pick: [count, exactly]. "up_to" (a number or an expression) allows anything from none to that many.
func _pick_rule(button: Dictionary, option_count: int) -> Array:
	if button.has("up_to"):
		var limit: Variant = button.up_to if button.up_to is float else game.evaluate(button.up_to, null)
		return [mini(int(limit), option_count), false]
	var how_many: Variant = button.get("pick", 1.0)
	return [int(how_many if how_many is float else game.evaluate(how_many, null)), true]


func _moves(action_name: String, choice_options: Array, how_many: int, only_exactly: bool) -> Array[Array]:
	var found: Array[Array] = []
	if choice_options.size() < how_many:
		return found
	var clicks := func(chosen_cards: Array) -> Array:
		return chosen_cards.map(func(card: Card) -> Dictionary: return {type = "click", card = card.id})
	if not only_exactly:
		for size in range(how_many + 1):
			for combination: Array in _combinations(choice_options, size):
				if allows(action_name, combination):
					found.append(clicks.call(combination) + [{type = "finish"}])
	elif not choice_options.all(func(option: Variant) -> bool: return option is Card):
		for option in choice_options:
			if allows(action_name, option):
				found.append([{type = "choose", option = option.name if option is Zone else option}])
	elif how_many == 1:
		for card: Card in choice_options:
			if allows(action_name, card):
				found.append(clicks.call([card]))
	else:
		for combination: Array in _combinations(choice_options, how_many):
			if allows(action_name, combination):
				found.append(clicks.call(combination))
	return found


static func _combinations(items: Array, size: int) -> Array:
	if size == 0:
		return [[]]
	if items.size() < size:
		return []
	var rest := items.slice(1)
	var with_first := _combinations(rest, size - 1).map(func(combination: Array) -> Array: return [items[0]] + combination)
	return with_first + _combinations(rest, size)

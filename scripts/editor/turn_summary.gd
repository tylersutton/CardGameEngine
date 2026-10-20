class_name TurnSummary
## The game's turn in words, for the editor: each step of "turn" in order, with its offers spelled out (which
## actions, done how), and then the actions that can be done any time and those only asked for by steps.

const INDENT := "    "


static func lines(rules: GameRules) -> Array[String]:
	var summary: Array[String] = []
	if rules.turn.is_empty():
		summary.append("No turn: players do whatever their actions allow, whenever they allow it.")
	else:
		summary.append("Each turn, for whoever's turn it is:")
		_describe_steps(rules, rules.turn, 1, summary)
	var offered := _offered_names(rules)
	var asked := rules.actions.keys().filter(func(action_name: String) -> bool:
		var action: Dictionary = rules.actions[action_name]
		return action_name not in offered and action.get("button", true) == false and not Plays.is_card_action(action))
	var any_time := rules.actions.keys().filter(func(action_name: String) -> bool: return action_name not in offered and action_name not in asked)
	if any_time:
		summary.append("Any time: " + ", ".join(any_time.map(func(action_name: String) -> String: return how(rules, action_name))))
	if asked:
		summary.append("Only when a step asks for it (ask): " + ", ".join(asked))
	return summary


## How an action is done: "Play a card (drag hand → pile)", "Draw (click deck)", "Pass (button)".
static func how(rules: GameRules, action_name: String) -> String:
	var action: Dictionary = rules.actions.get(action_name, {})
	var ways: Array[String] = []
	if action.get("drag") is Dictionary:
		ways.append("drag %s → %s" % [_zones_text(action.drag.get("from")), _zones_text(action.drag.get("to"))])
	for click_key: String in ["click", "double_click"]:
		var binding: Variant = action.get(click_key)
		var word := click_key.replace("_", "-")
		if binding is bool and binding:
			ways.append("or %s it" % word)
		elif binding != null and binding is not bool:
			ways.append("%s %s" % [word, _zones_text(binding)])
	if action.get("button", not Plays.is_card_action(action)) == true:
		ways.append("button")
	return "%s (%s)" % [action_name, " ".join(ways) if ways else "button"]


static func _describe_steps(rules: GameRules, steps: Array, depth: int, summary: Array[String]) -> void:
	for step: Variant in steps:
		var indent := INDENT.repeat(depth)
		if step is String:
			summary.append(indent + _describe_step(rules, step))
		elif step is Dictionary and step.has("if"):
			summary.append(indent + "If %s:" % step["if"])
			_describe_steps(rules, step.get("then", []), depth + 1, summary)
			if step.get("else", []):
				summary.append(indent + "Otherwise:")
				_describe_steps(rules, step.else, depth + 1, summary)
		elif step is Dictionary and step.has("for"):
			summary.append(indent + "For each %s in %s:" % [step.for, step.in])
			_describe_steps(rules, step.get("do", []), depth + 1, summary)
		elif step is Dictionary and step.has("while"):
			summary.append(indent + "Again and again while %s:" % step.while)
			_describe_steps(rules, step.get("do", []), depth + 1, summary)


## A step as written, unless it offers actions: then which ones, and how each is done.
static func _describe_step(rules: GameRules, step: String) -> String:
	var parsed := StepParser.parse(step)
	if parsed.has("error"):
		return step
	for call: Dictionary in _calls(parsed.tree):
		var names := _names_given(call.args[0]) if call.args else []
		var hows := names.map(func(action_name: String) -> String: return how(rules, action_name))
		if call.name == "offer":
			return "Offer " + " or ".join(hows)
		if call.name == "offer_until" and call.args.size() == 2:
			var enders := _names_given(call.args[1]).map(func(action_name: String) -> String: return how(rules, action_name))
			return "Offer %s again and again, until %s" % [" or ".join(hows), " or ".join(enders)]
	return step


static func _calls(node: Dictionary) -> Array:
	var found := [node] if node.kind == "call" and node.name in StepCompiler.OFFER_BLOCKS else []
	for child in StepCompiler.children(node):
		found.append_array(_calls(child))
	return found


static func _names_given(arg: Dictionary) -> Array:
	var items: Array = arg.items if arg.kind == "list" else [arg]
	return items.filter(func(item: Dictionary) -> bool: return item.kind == "value" and item.value is String).map(func(item: Dictionary) -> String: return item.value)


## Every action named in an offer anywhere: they can only be done while offered.
static func _offered_names(rules: GameRules) -> Array:
	var names := []
	var texts: Array[String] = []
	_collect_texts(rules.turn, texts)
	for steps: Array in rules.triggers.values():
		_collect_texts(steps, texts)
	for action: Dictionary in rules.actions.values():
		_collect_texts(action.get("steps", []), texts)
	for text in texts:
		var parsed := StepParser.parse(text)
		for call: Dictionary in _calls(parsed.get("tree", {kind = "value", value = null})):
			for arg: Dictionary in call.args.slice(0, 2):
				names.append_array(_names_given(arg))
	return names


static func _collect_texts(steps: Variant, texts: Array[String]) -> void:
	for step: Variant in steps if steps is Array else [steps]:
		if step is String:
			texts.append(step)
		elif step is Dictionary:
			for key in ["if", "in", "while"]:
				if step.get(key) is String:
					texts.append(step[key])
			for key in ["then", "else", "do"]:
				_collect_texts(step.get(key, []), texts)


static func _zones_text(zones: Variant) -> String:
	if zones == null:
		return "anywhere"
	return ", ".join(zones) if zones is Array else str(zones)

class_name StepRunner
extends RefCounted
## Runs a game's lists of steps one step at a time from a stack of frames (innermost last), so that a step can
## wait for a player (choose(...) or offer(...)) and carry on once they've answered. Frames are plain data, so
## undo, bots and the state hash copy them like the rest of the game. When nothing is left to run, the game's
## "turn" steps start again (see _start_turn).
##
## A frame is one list of steps being run: {steps, index (the next step), card, depth (how many triggers deep),
## root (true for a whole trigger, action or rule; false for an "if" branch or loop body inside one), context
## (from, to, clicked_zone and choice when it started), locals (names set with = that aren't game variables, and
## loop items), answers (choices already made for the step at index), kind ("list", "loop" or "while")}, and
## turn = true for the "turn" steps (and the "if" branches and loops inside them).
## A loop frame also has loop_name, items, position and outer_player; a while frame has condition, repeats and checked.
##
## A step that starts more steps (trigger('deal'), a card's effect, try_move's rule) schedules them: they run, in
## order, right after that step. Steps started from outside (an input) run at once.

const MAX_REPEATS := 1000
const MAX_DEPTH := 50

## Held weakly: the game holds its runner, and a reference each way would keep both alive forever.
var game: Game:
	get: return game_reference.get_ref()
var game_reference: WeakRef
var frames: Array[Dictionary] = []
var scheduled: Array[Dictionary] = []
var running := false
## The frame whose step is being worked out, and how many of its answers that step has used so far.
var stepping: Dictionary = {}
var answers_used := 0
## Set by choose(...) or offer(...) when its step has no answer yet: what to ask for.
var wait_request: Dictionary = {}
## While above 0, steps are only scheduled (see hold).
var holding := 0
## A step is waiting for its answer (the innermost frame's): nothing runs until it gets one, or until steps are
## scheduled on top of it (an action that was never offered, done while an offer waits).
var waiting := false
var waited_since_turn := false
var turns_run := 0
var idle_turns := 0
var turns_stopped := false


func _init(game_to_run: Game) -> void:
	game_reference = weakref(game_to_run)


func is_waiting() -> bool:
	return not frames.is_empty() and not running


## Steps to run after the step being worked out, or straight away when started from outside (see drive).
func schedule(steps: Array, card: Card) -> void:
	if steps.is_empty():
		return
	var depth: int = stepping.depth + 1 if stepping else 0
	if depth >= MAX_DEPTH:
		game.report_failure("Stopped: steps triggered each other %d levels deep (endless loop?)" % MAX_DEPTH)
		return
	scheduled.append(_frame(steps, card, depth, true, game.move_context(), {}))


## Runs frames until none are left or a step waits. A step that was waiting is worked out again on the way,
## so it waits again (or carries on, if it has its answer now).
func drive() -> void:
	if running or holding > 0 or waiting and scheduled.is_empty():
		return
	waiting = false
	running = true
	_run_frames(0)
	running = false


## Steps started until release() are only scheduled, then run together in order: a drag's card effects and its
## action's steps, so an effect that waits for a choice holds up the rest.
func hold() -> void:
	holding += 1


func release() -> void:
	holding -= 1
	drive()


## Runs steps to the end at once, e.g. "deck_empty" in the middle of a draw. They can't wait for a choice.
func run_now(steps: Array, card: Card) -> void:
	var base := frames.size()
	var outer := {running = running, stepping = stepping, answers_used = answers_used, scheduled = scheduled, context = game.move_context()}
	frames.append(_frame(steps, card, stepping.depth + 1 if stepping else 0, true, outer.context, {}))
	scheduled = []
	running = true
	if not _run_frames(base):
		game.report_failure("Steps run in the middle of another step (like \"deck_empty\" during a draw) can't wait for a choice")
		_end_script(base)
	running = outer.running
	stepping = outer.stepping
	answers_used = outer.answers_used
	scheduled = outer.scheduled
	game.set_move_context(outer.context)


## The player's choice for the waiting step: that step runs again, and choose(...) gives this answer.
func answer(choice: Variant) -> void:
	accept(choice)
	drive()


## Gives the waiting step its answer without running it yet, e.g. an offer's action, whose steps run first.
func accept(answer_given: Variant) -> void:
	frames.back().answers.append(answer_given)
	waiting = false


## Whether the step being worked out already has an answer for its next choose(...) or offer(...).
func has_answer() -> bool:
	return not stepping.is_empty() and answers_used < stepping.answers.size()


## choose(...) during a step: the answer already given, or null after asking for one (the step then stops).
func answer_or_wait(request: Dictionary) -> Variant:
	if stepping.is_empty():
		game.report_failure("choose(...) only works in steps")
		return null
	if answers_used < stepping.answers.size():
		answers_used += 1
		return stepping.answers[answers_used - 1]
	wait_request = request
	return null


func has_local(name: String) -> bool:
	return _frame_holding(name) != -1


func local(name: String) -> Variant:
	return frames[_frame_holding(name)].locals[name]


## Sets a remembered name in the innermost list of the running trigger or rule that has it, or else in its
## outermost list, so the rest of that trigger or rule can read it.
func set_local(name: String, value: Variant) -> void:
	var holder := _frame_holding(name)
	frames[holder if holder != -1 else _root_index()].locals[name] = value


## The frames as copies whose remembered names and answers can change without touching these (for undo).
static func copy_frames(original: Array[Dictionary]) -> Array[Dictionary]:
	var copies: Array[Dictionary] = []
	for frame in original:
		var copy := frame.duplicate()
		copy.locals = frame.locals.duplicate()
		copy.answers = frame.answers.duplicate()
		copies.append(copy)
	return copies


func _frame(steps: Array, card: Card, depth: int, root: bool, context: Dictionary, locals: Dictionary) -> Dictionary:
	return {steps = steps, index = 0, card = card, depth = depth, root = root, context = context, locals = locals, answers = [], kind = "list"}


## Runs the frames above base until they're done (true) or a step waits (false).
func _run_frames(base: int) -> bool:
	_push_scheduled()
	while frames.size() > base or base == 0 and _start_turn():
		_run_step(frames.back())
		if not wait_request.is_empty():
			var request := wait_request
			wait_request = {}
			if base > 0:
				return false
			waiting = true
			waited_since_turn = true
			if request.get("kind") == "offer":
				game.open_offer(request)
			else:
				game.open_inline_choice(request)
			return false
		_push_scheduled()
	return true


## Starts the "turn" steps again, if the game has them and nothing else is due (Game.next_turn_due). When a whole
## round of turns goes by without anyone able to do anything (every offer had nothing possible), it's a stalemate,
## and the game is over.
func _start_turn() -> bool:
	if turns_stopped or not game.next_turn_due():
		return false
	if turns_run > 0:
		idle_turns = 0 if waited_since_turn else idle_turns + 1
	waited_since_turn = false
	if idle_turns >= maxi(game.players_left().size(), 1):
		game.log_line("Nobody could do anything for a whole round of turns, so the game is over")
		game.end_game()
		return false
	turns_run += 1
	game.log_line("[turn] " + game.player_name(game.current_player()))
	var turn := _frame(game.rules.turn, null, 0, true, game.move_context(), {})
	turn.turn = true
	frames.append(turn)
	return true


## Steps scheduled during the last step go on top, first one innermost-last, so they run in the order scheduled.
func _push_scheduled() -> void:
	for i in range(scheduled.size() - 1, -1, -1):
		frames.append(scheduled[i])
	scheduled.clear()


func _run_step(frame: Dictionary) -> void:
	if game.over and frame.get("turn", false):
		# The game ended (in this turn, or in an action done in it): the rest of the turn doesn't happen.
		_end_script(_root_index(), false)
		return
	game.set_move_context(frame.context)
	if frame.kind == "while" and not frame.checked:
		_check_while(frame)
		return
	if frame.index >= frame.steps.size():
		_finish_list(frame)
		return
	var step: Variant = frame.steps[frame.index]
	if step is String and step.strip_edges().is_empty():
		frame.index += 1
		return
	var text: String = step if step is String else step.get("if", step.get("in", step.get("while", "")))
	if step is Dictionary and step.has("while"):
		frame.index += 1
		frames.append(_body(frame, step.get("do", []), {kind = "while", condition = text, repeats = 0, checked = false}))
		return
	var result: Variant = _work_out(frame, text)
	if not wait_request.is_empty():
		return
	if game.evaluation_failed:
		_end_script(_root_index())
		return
	frame.index += 1
	if frame.answers:
		frame.answers = []
	if step is Dictionary and step.has("for"):
		_start_loop(frame, step, result)
	elif step is Dictionary:
		var branch := "then" if StepEvaluator.is_true(result) else "else"
		game.log_line("  if %s → %s" % [text, branch])
		if step.get(branch, []):
			frames.append(_body(frame, step[branch], {}))


## Works out one step's text, with the answers to any choose(...) in it made so far.
func _work_out(frame: Dictionary, text: String) -> Variant:
	stepping = frame
	answers_used = 0
	var result: Variant = game.evaluate(text, frame.card)
	stepping = {}
	game.game_log.flush_items()
	return result


func _body(parent: Dictionary, steps: Array, extra: Dictionary) -> Dictionary:
	var body := _frame(steps, parent.card, parent.depth, false, parent.context, {})
	body.turn = parent.get("turn", false)
	body.merge(extra, true)
	return body


func _start_loop(frame: Dictionary, step: Dictionary, items: Variant) -> void:
	var item_list: Variant = _loop_items(items)
	if item_list == null:
		game.report_failure("Can't loop over \"%s\": it should be a zone, a list, or a number" % step.in)
		_end_script(_root_index())
		return
	game.log_line("  for %s in %s (%d)" % [step.for, step.in, item_list.size()])
	if item_list.is_empty() or step.get("do", []).is_empty():
		return
	var loop := _body(frame, step.do, {kind = "loop", loop_name = step.for, items = item_list, position = 0})
	loop.outer_player = game.variables.get(Game.PLAYER_VARIABLE)
	frames.append(loop)
	_set_loop_item(loop)


## Looping over "player" changes whose turn it is, so per-player names follow the loop.
func _set_loop_item(loop: Dictionary) -> void:
	var item: Variant = loop.items[loop.position]
	if loop.loop_name == Game.PLAYER_VARIABLE:
		game.variables[Game.PLAYER_VARIABLE] = item
	else:
		loop.locals[loop.loop_name] = item
	loop.locals[loop.loop_name + Game.LOOP_NUMBER_SUFFIX] = loop.position + 1


func _check_while(frame: Dictionary) -> void:
	var holds := StepEvaluator.is_true(_work_out(frame, frame.condition))
	if not wait_request.is_empty():
		return
	if game.evaluation_failed:
		_end_script(_root_index())
	elif not holds:
		game.log_line("  while %s: repeated %d times" % [frame.condition, frame.repeats])
		frames.pop_back()
	elif frame.repeats >= MAX_REPEATS:
		game.report_failure("Stopped: \"while %s\" was still true after %d repeats" % [frame.condition, MAX_REPEATS])
		_end_script(_root_index())
	else:
		frame.answers = []
		frame.repeats += 1
		frame.checked = true


## The end of a list: a loop goes on to its next item, a while checks again, anything else is done.
func _finish_list(frame: Dictionary) -> void:
	if frame.kind == "loop" and frame.position + 1 < frame.items.size():
		frame.position += 1
		frame.index = 0
		_set_loop_item(frame)
	elif frame.kind == "while":
		frame.index = 0
		frame.checked = false
	else:
		_pop()


## Stops the running trigger or rule (after a failure): its frames from index on are dropped. A failure in the
## turn stops the turns, rather than failing again every turn.
func _end_script(index: int, failed := true) -> void:
	if failed and index < frames.size() and frames[index].get("turn", false):
		game.report_failure("Turns stopped after that failure; fix the step and press Reload")
		turns_stopped = true
	while frames.size() > index:
		_pop()


func _pop() -> void:
	var frame: Dictionary = frames.pop_back()
	if frame.kind == "loop" and frame.loop_name == Game.PLAYER_VARIABLE:
		game.variables[Game.PLAYER_VARIABLE] = frame.outer_player


## The outermost frame of the trigger or rule that the innermost frame belongs to.
func _root_index() -> int:
	for i in range(frames.size() - 1, -1, -1):
		if frames[i].root:
			return i
	return 0


func _frame_holding(name: String) -> int:
	for i in range(frames.size() - 1, -1, -1):
		if name in frames[i].locals:
			return i
		if frames[i].root:
			return -1
	return -1


static func _loop_items(items: Variant) -> Variant:
	if items is Zone:
		return items.cards.duplicate()
	if items is Array:
		return items.duplicate()
	if items is int or items is float:
		return range(1, int(items) + 1)
	return null

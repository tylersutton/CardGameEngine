class_name ScriptErrors
extends Logger
## Collects the errors Godot reports while a test runs (a call to a function that's gone, a script that didn't
## compile), so the test fails on them even when its own checks pass. Warnings don't count.

var messages: Array[String] = []
var mutex := Mutex.new()


## Starts collecting; the test adds messages to its failures before it reports.
static func watch() -> ScriptErrors:
	var errors := ScriptErrors.new()
	OS.add_logger(errors)
	return errors


func _log_error(_function: String, file: String, line: int, code: String, rationale: String, _editor_notify: bool, error_type: int, _script_backtraces: Array[ScriptBacktrace]) -> void:
	if error_type == ERROR_TYPE_WARNING:
		return
	mutex.lock()
	messages.append("Godot reported an error: %s (%s:%d)" % [rationale if rationale else code, file.get_file(), line])
	mutex.unlock()

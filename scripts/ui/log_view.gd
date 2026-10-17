class_name LogView
extends RichTextLabel

const ERROR_COLOR := Color(1, 0.45, 0.45)


func add(text: String) -> void:
	add_text(text + "\n")


func add_error(text: String) -> void:
	push_color(ERROR_COLOR)
	add_text(text + "\n")
	pop()

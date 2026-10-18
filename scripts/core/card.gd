class_name Card
extends RefCounted

const TITLE_COLUMN := "name"

var id := -1
var data: Dictionary
var face_up := true
var marks: Dictionary = {}

var title: String:
	get: return data.get(TITLE_COLUMN, "")


func _init(row: Dictionary) -> void:
	data = row

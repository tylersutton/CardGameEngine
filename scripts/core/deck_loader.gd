class_name DeckLoader

const COUNT_COLUMN := "count"


static func load_csv(path: String) -> Array[Card]:
	var file := FileAccess.open(path, FileAccess.READ)
	assert(file, "Could not open deck: " + path)
	var headers := file.get_csv_line()
	var cards: Array[Card] = []
	while not file.eof_reached():
		var values := file.get_csv_line()
		if values.size() != headers.size():
			continue
		var row := {}
		for i in headers.size():
			row[headers[i]] = values[i]
		for copy in _copies(row):
			cards.append(Card.new(row.duplicate()))
	return cards


static func _copies(row: Dictionary) -> int:
	var count: String = row.get(COUNT_COLUMN, "")
	row.erase(COUNT_COLUMN)
	return int(count) if count else 1

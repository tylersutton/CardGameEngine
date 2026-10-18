class_name DeckLoader

const COUNT_COLUMN := "count"
const RECEIVED_DECK := "user://received_deck.csv"


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


## For a deck received as text: Godot only reads CSV (with its quoted fields) from files, so it goes through one.
static func parse_csv(text: String) -> Array[Card]:
	var file := FileAccess.open(RECEIVED_DECK, FileAccess.WRITE)
	file.store_string(text)
	file.close()
	return load_csv(RECEIVED_DECK)


static func _copies(row: Dictionary) -> int:
	var count: String = row.get(COUNT_COLUMN, "")
	row.erase(COUNT_COLUMN)
	return int(count) if count else 1

class_name DeckLoader

const COUNT_COLUMN := "count"
const RECEIVED_DECK := "user://received_deck_%d.csv"


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
	var number_columns := {}
	for column in headers:
		if column != COUNT_COLUMN and cards.all(func(card: Card) -> bool: return Card.is_number(card.data.get(column, ""))):
			number_columns[column] = true
	for card in cards:
		card.number_columns = number_columns
	return cards


## For a deck received as text: Godot only reads CSV (with its quoted fields) from files, so it goes through one.
## Each running copy uses its own file, since copies on one computer share user://.
static func parse_csv(text: String) -> Array[Card]:
	var path := RECEIVED_DECK % OS.get_process_id()
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string(text)
	file.close()
	var cards := load_csv(path)
	DirAccess.remove_absolute(path)
	return cards


static func _copies(row: Dictionary) -> int:
	var count: String = row.get(COUNT_COLUMN, "")
	row.erase(COUNT_COLUMN)
	return int(count) if count else 1

class_name DeckLoader

const COUNT_COLUMN := "count"
const RECEIVED_DECK := "user://received_deck_%d.csv"


static func load_csv(path: String) -> Array[Card]:
	var file := FileAccess.open(path, FileAccess.READ)
	assert(file, "Could not open deck: " + path)
	var headers := file.get_csv_line()
	var count_column := _column_named(headers, COUNT_COLUMN)
	var title_column := _column_named(headers, Card.TITLE_COLUMN)
	if title_column not in headers:
		title_column = headers[0] if headers[0] != count_column or headers.size() == 1 else headers[1]
	var cards: Array[Card] = []
	while not file.eof_reached():
		var values := file.get_csv_line()
		if values.size() != headers.size():
			continue
		var row := {}
		for i in headers.size():
			row[headers[i]] = values[i]
		var count: String = row.get(count_column, "")
		row.erase(count_column)
		# Blank lines and spreadsheet extras like a totals row aren't cards.
		if row.values().all(func(value: String) -> bool: return value.strip_edges().is_empty()):
			continue
		for copy in int(count) if count else 1:
			var card := Card.new(row.duplicate())
			card.title_column = title_column
			cards.append(card)
	var number_columns := {}
	for column in headers:
		if column != count_column and cards.all(func(card: Card) -> bool: return Card.is_number(card.data.get(column, ""))):
			number_columns[column] = true
	for card in cards:
		card.number_columns = number_columns
	return cards


## The special columns ("count", "name") are found whatever their capitals ("Count").
static func _column_named(headers: PackedStringArray, wanted: String) -> String:
	for header in headers:
		if header.to_lower() == wanted:
			return header
	return wanted


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


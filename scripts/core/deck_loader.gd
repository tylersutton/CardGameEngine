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


## A deck's rows as lists of text (the first row is the column names), e.g. for the editor's deck table.
static func rows(text: String) -> Array[PackedStringArray]:
	var path := RECEIVED_DECK % OS.get_process_id()
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string(text)
	file.close()
	file = FileAccess.open(path, FileAccess.READ)
	var found: Array[PackedStringArray] = []
	while not file.eof_reached():
		var row := file.get_csv_line()
		if not (row.size() == 1 and row[0].is_empty()):
			found.append(row)
	file.close()
	DirAccess.remove_absolute(path)
	return found


## Rows back into CSV text, quoting values that need it.
static func to_csv(table: Array) -> String:
	var lines := table.map(func(row: PackedStringArray) -> String:
		return ",".join(Array(row).map(func(value: String) -> String:
			var needs_quotes := value.contains(",") or value.contains("\"") or value.contains("\n")
			return "\"%s\"" % value.replace("\"", "\"\"") if needs_quotes else value)))
	return "\n".join(lines) + "\n"


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


class_name TableView
extends Control

const FELT_COLOR := Color(0.12, 0.38, 0.22)
const LABEL_COLOR := Color(1, 1, 1, 0.7)
const LABEL_FONT_SIZE := 14
const LABEL_GAP := 22.0

static var slot_style := _slot_box()

var game: Game
var views: Dictionary[Card, CardView] = {}


func _ready() -> void:
	resized.connect(_layout_all)


func show_game(game_to_show: Game) -> void:
	for view in views.values():
		view.queue_free()
	views.clear()
	game = game_to_show
	for zone: Zone in game.zones.values():
		zone.changed.connect(_layout_zone)
		zone.changed.connect(queue_redraw.unbind(1))
	game.variables_changed.connect(queue_redraw)
	game.variables_changed.connect(_refresh_views)
	game.state_changed.connect(_refresh_views)
	_layout_all()
	queue_redraw()


func _layout_all() -> void:
	if game:
		for zone: Zone in game.zones.values():
			_layout_zone(zone)


func _layout_zone(zone: Zone) -> void:
	var center := zone.anchor * size
	var groups := _groups(zone)
	var group_centers := ZoneLayout.group_centers(groups.map(func(group: Array) -> int: return group.size()))
	for group_index in groups.size():
		var group: Array = groups[group_index]
		for i in group.size():
			var view := _view_for(group[i], center)
			view.face_up = game.shows_face(group[i])
			move_child(view, -1)
			var placement := ZoneLayout.place(zone.layout, i, group.size(), _max_width(zone))
			var group_offset := Vector2(group_centers[group_index], 0)
			view.move_to(center + group_offset + placement.origin - CardView.SIZE / 2, placement.get_rotation())


func _groups(zone: Zone) -> Array:
	if zone.layout != "melds":
		return [zone.cards]
	var deadwood := game.meld_rules.deadwood(zone.cards)
	return game.meld_rules.best(zone.cards) + ([deadwood] if deadwood else [])


func _refresh_views() -> void:
	for card in views:
		views[card].face_up = game.shows_face(card)
		views[card].highlighted = card in game.choices


func _view_for(card: Card, spawn_center: Vector2) -> CardView:
	if card not in views:
		var view := CardView.new(card, game.rules.card_face)
		view.position = spawn_center - CardView.SIZE / 2
		view.clicked.connect(_on_card_clicked)
		view.dropped.connect(_on_card_dropped)
		add_child(view)
		views[card] = view
	return views[card]


func _on_card_clicked(view: CardView) -> void:
	var zone := game.zone_of(view.card)
	game.click(view.card)
	_layout_zone(zone)


func _on_card_dropped(view: CardView) -> void:
	var source := game.zone_of(view.card)
	var drop_point := get_local_mouse_position()
	var destination := _nearest_zone(drop_point)
	if destination == source and source.layout in ["row", "fan"]:
		game.reorder(view.card, _nearest_slot(source, drop_point))
	elif destination == source or not game.drag(view.card, destination):
		_layout_zone(source)


func _nearest_slot(zone: Zone, point: Vector2) -> int:
	var offset := point.x - zone.anchor.x * size.x
	var nearest := 0
	var nearest_distance := INF
	for i in zone.cards.size():
		var distance := absf(ZoneLayout.place(zone.layout, i, zone.cards.size(), _max_width(zone)).origin.x - offset)
		if distance < nearest_distance:
			nearest = i
			nearest_distance = distance
	return nearest


func _max_width(zone: Zone) -> float:
	return zone.width * size.x if zone.width else ZoneLayout.MAX_WIDTH


func _nearest_zone(point: Vector2) -> Zone:
	var zones := game.zones.values()
	zones.sort_custom(func(a: Zone, b: Zone) -> bool:
		return point.distance_to(a.anchor * size) < point.distance_to(b.anchor * size))
	return zones[0]


func _draw() -> void:
	draw_rect(Rect2(Vector2.ZERO, size), FELT_COLOR)
	if not game:
		return
	var font := get_theme_default_font()
	for zone: Zone in game.zones.values():
		if zone.layout == "melds" and zone.is_empty():
			continue
		var slot :=Rect2(zone.anchor * size - CardView.SIZE / 2, CardView.SIZE)
		draw_style_box(slot_style, slot)
		var label := "%s (%d)" % [zone.name, zone.cards.size()]
		if game.rules.players > 1 and zone.owner == game.current_player():
			label = "▶ " + label
		draw_string(font, slot.position - Vector2(0, LABEL_GAP), label, HORIZONTAL_ALIGNMENT_CENTER, slot.size.x, LABEL_FONT_SIZE, LABEL_COLOR)


static func _slot_box() -> StyleBoxFlat:
	var box := StyleBoxFlat.new()
	box.bg_color = Color(0, 0, 0, 0.15)
	box.border_color = Color(1, 1, 1, 0.25)
	box.set_border_width_all(2)
	box.set_corner_radius_all(8)
	return box

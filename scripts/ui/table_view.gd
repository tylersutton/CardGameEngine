class_name TableView
extends Control

const FELT_COLOR := Color(0.12, 0.38, 0.22)
const LABEL_COLOR := Color(1, 1, 1, 0.7)
const LABEL_FONT_SIZE := 14
const LABEL_GAP := 22.0
const BOTTOM_MARGIN := 8.0
const LABEL_OVERHANG := 20.0

static var slot_style := _slot_box()

var game: Game
var views: Dictionary[Card, CardView] = {}
var carried_views: Array[CardView] = []
var carried_offsets: Array[Vector2] = []


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
	var turn := deg_to_rad(zone.rotation)
	var groups := _groups(zone)
	var group_centers := ZoneLayout.group_centers(groups.map(func(group: Array) -> int: return group.size()))
	for group_index in groups.size():
		var group: Array = groups[group_index]
		for i in group.size():
			var view := _view_for(group[i], center)
			view.face_up = game.shows_face(group[i])
			move_child(view, -1)
			var placement := ZoneLayout.place(zone.layout, i, group.size(), _max_spread(zone))
			var offset := (Vector2(group_centers[group_index], 0) + placement.origin).rotated(turn)
			view.move_to(center + offset - CardView.SIZE / 2, placement.get_rotation() + turn)


func _groups(zone: Zone) -> Array:
	if zone.layout != "melds":
		return [zone.cards]
	var deadwood := game.meld_rules.deadwood(zone.cards)
	return game.meld_rules.best(zone.cards) + ([deadwood] if deadwood else [])


func _refresh_views() -> void:
	for card in views:
		views[card].face_up = game.shows_face(card)
		views[card].highlighted = card in game.choices
		views[card].selected = card in game.selected


func _view_for(card: Card, spawn_center: Vector2) -> CardView:
	if card not in views:
		var view := CardView.new(card, game.rules.card_face)
		view.position = spawn_center - CardView.SIZE / 2
		view.picked_up.connect(_on_card_picked_up)
		view.dragged.connect(_on_card_dragged)
		view.clicked.connect(_on_card_clicked)
		view.dropped.connect(_on_card_dropped)
		add_child(view)
		views[card] = view
	return views[card]


func _on_card_picked_up(view: CardView) -> void:
	carried_views.clear()
	carried_offsets.clear()
	for card in game.carried(view.card).slice(1):
		var carried_view := views[card]
		carried_view.stop_moving()
		move_child(carried_view, -1)
		carried_views.append(carried_view)
		carried_offsets.append(carried_view.position - view.position)


func _on_card_dragged(view: CardView) -> void:
	for i in carried_views.size():
		carried_views[i].position = view.position + carried_offsets[i]


func _on_card_clicked(view: CardView) -> void:
	carried_views.clear()
	var zone := game.zone_of(view.card)
	game.click(view.card)
	_layout_zone(zone)


func _on_card_dropped(view: CardView) -> void:
	var source := game.zone_of(view.card)
	var drop_point := get_local_mouse_position()
	var dragged_views: Array[CardView] = [view]
	dragged_views.append_array(carried_views)
	var destination := _nearest_zone(drop_point, dragged_views)
	carried_views.clear()
	if destination == source and source.layout in ["row", "fan"]:
		game.reorder(view.card, _nearest_slot(source, drop_point))
	elif destination == source or not game.drag(view.card, destination):
		_layout_zone(source)


func _gui_input(event: InputEvent) -> void:
	if game and event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		for zone: Zone in game.zones.values():
			if zone.is_empty() and _slot(zone).has_point(event.position):
				game.click_empty(zone)


func _nearest_slot(zone: Zone, point: Vector2) -> int:
	var offset := point.x - zone.anchor.x * size.x
	var nearest := 0
	var nearest_distance := INF
	for i in zone.cards.size():
		var distance := absf(ZoneLayout.place(zone.layout, i, zone.cards.size(), _max_spread(zone)).origin.x - offset)
		if distance < nearest_distance:
			nearest = i
			nearest_distance = distance
	return nearest


func _max_spread(zone: Zone) -> float:
	if zone.layout == "column":
		return size.y * (1 - zone.anchor.y) - CardView.SIZE.y / 2 - BOTTOM_MARGIN
	var sideways := is_equal_approx(absf(sin(deg_to_rad(zone.rotation))), 1.0)
	return zone.width * (size.y if sideways else size.x) if zone.width else ZoneLayout.MAX_WIDTH


func _nearest_zone(point: Vector2, ignored_views: Array[CardView]) -> Zone:
	var nearest: Zone
	var nearest_distance := INF
	for zone: Zone in game.zones.values():
		var centers: Array[Vector2] = [zone.anchor * size]
		for card in zone.cards:
			if views[card] not in ignored_views:
				centers.append(views[card].position + CardView.SIZE / 2)
		for center in centers:
			if point.distance_to(center) < nearest_distance:
				nearest = zone
				nearest_distance = point.distance_to(center)
	return nearest


func _slot(zone: Zone) -> Rect2:
	return Rect2(zone.anchor * size - CardView.SIZE / 2, CardView.SIZE)


func _draw() -> void:
	draw_rect(Rect2(Vector2.ZERO, size), FELT_COLOR)
	if not game:
		return
	var font := get_theme_default_font()
	for zone: Zone in game.zones.values():
		if zone.layout == "melds" and zone.is_empty():
			continue
		var slot := _slot(zone)
		draw_style_box(slot_style, slot)
		var label := "%s (%d)" % [zone.name, zone.cards.size()]
		if game.rules.players > 1 and zone.owner == game.current_player():
			label = "▶ " + label
		var label_position := slot.position - Vector2(LABEL_OVERHANG, LABEL_GAP)
		draw_string(font, label_position, label, HORIZONTAL_ALIGNMENT_CENTER, slot.size.x + LABEL_OVERHANG * 2, LABEL_FONT_SIZE, LABEL_COLOR)


static func _slot_box() -> StyleBoxFlat:
	var box := StyleBoxFlat.new()
	box.bg_color = Color(0, 0, 0, 0.15)
	box.border_color = Color(1, 1, 1, 0.25)
	box.set_border_width_all(2)
	box.set_corner_radius_all(8)
	return box

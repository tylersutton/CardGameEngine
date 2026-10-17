class_name ZoneLayout

const PILE_STEP := 0.25
const ROW_SPACING := 110.0
const FAN_SPACING := 50.0
const FAN_RADIUS := 1200.0
const MAX_WIDTH := 800.0
const MELD_SPACING := 26.0
const MELD_GAP := 24.0


static func place(layout: String, index: int, count: int, max_width := MAX_WIDTH) -> Transform2D:
	var steps_from_middle := index - (count - 1) / 2.0
	var widest_spacing := (max_width - CardView.SIZE.x) / maxf(count - 1, 1)
	match layout:
		"pile":
			return Transform2D(0.0, Vector2(0, -index * PILE_STEP))
		"fan":
			var x := steps_from_middle * minf(FAN_SPACING, widest_spacing)
			return Transform2D(x / FAN_RADIUS, Vector2(x, x * x / (2 * FAN_RADIUS)))
		"melds":
			return Transform2D(0.0, Vector2(steps_from_middle * MELD_SPACING, 0))
		_:
			var x := steps_from_middle * minf(ROW_SPACING, widest_spacing)
			return Transform2D(0.0, Vector2(x, 0))


static func group_centers(group_sizes: Array) -> Array[float]:
	var widths := group_sizes.map(func(cards: int) -> float: return (cards - 1) * MELD_SPACING + CardView.SIZE.x)
	var total_width: float = widths.reduce(func(total: float, width: float) -> float: return total + width, 0.0)
	var left := -(total_width + MELD_GAP * (widths.size() - 1)) / 2
	var centers: Array[float] = []
	for width: float in widths:
		centers.append(left + width / 2)
		left += width + MELD_GAP
	return centers

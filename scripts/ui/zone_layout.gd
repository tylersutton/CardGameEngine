class_name ZoneLayout

const PILE_STEP := 0.25
const ROW_SPACING := 110.0
const FAN_SPACING := 50.0
const FAN_RADIUS := 1200.0
const MAX_WIDTH := 800.0
const MELD_SPACING := 26.0
const COLUMN_SPACING := 30.0
const FACE_DOWN_SPACING := 12.0
const PILE_SPREAD := 25.0
const MELD_GAP := 24.0


static func place(layout: String, index: int, count: int, max_spread := MAX_WIDTH, spread_top := 0) -> Transform2D:
	var steps_from_middle := index - (count - 1) / 2.0
	var widest_spacing := (max_spread - CardView.SIZE.x) / maxf(count - 1, 1)
	match layout:
		"pile":
			return Transform2D(0.0, Vector2(maxi(0, index - count + spread_top) * PILE_SPREAD, -index * PILE_STEP))
		"fan":
			var x := steps_from_middle * minf(FAN_SPACING, widest_spacing)
			return Transform2D(x / FAN_RADIUS, Vector2(x, x * x / (2 * FAN_RADIUS)))
		"melds":
			return Transform2D(0.0, Vector2(steps_from_middle * MELD_SPACING, 0))
		_:
			var x := steps_from_middle * minf(ROW_SPACING, widest_spacing)
			return Transform2D(0.0, Vector2(x, 0))


## How far down each card of a column sits: face-down cards overlap more, and a long column squeezes to fit.
static func column_offsets(faces_up: Array, max_length: float) -> Array[float]:
	var offsets: Array[float] = [0.0]
	for i in range(1, faces_up.size()):
		offsets.append(offsets[i - 1] + (COLUMN_SPACING if faces_up[i - 1] else FACE_DOWN_SPACING))
	var squeeze := minf(1.0, max_length / offsets.back()) if offsets.back() > 0 else 1.0
	var squeezed: Array[float] = []
	for offset in offsets:
		squeezed.append(offset * squeeze)
	return squeezed


static func group_centers(group_sizes: Array) -> Array[float]:
	var widths := group_sizes.map(func(cards: int) -> float: return (cards - 1) * MELD_SPACING + CardView.SIZE.x)
	var total_width: float = widths.reduce(func(total: float, width: float) -> float: return total + width, 0.0)
	var left := -(total_width + MELD_GAP * (widths.size() - 1)) / 2
	var centers: Array[float] = []
	for width: float in widths:
		centers.append(left + width / 2)
		left += width + MELD_GAP
	return centers

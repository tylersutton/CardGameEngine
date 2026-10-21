using Core;
using Godot;

/// Laid over the table in the editor's Edit mode: shows every zone's outline and name, and lets you pick a zone
/// (ZonePicked) or drag it somewhere else (ZoneMoved, with its new center as fractions of the table).
public partial class ZoneDragger : Control
{
    static readonly Color OutlineColor = new(1, 1, 1, 0.5f);
    static readonly Color SelectedColor = new(1, 0.85f, 0.3f);
    static readonly Color NameColor = new(1, 1, 1, 0.9f);
    const int FontSize = 13;
    const float DragThreshold = 4;

    public event Action<Zone>? ZonePicked;
    public event Action<Zone, Vector2>? ZoneMoved;

    /// The kind of zone selected in the editor, outlined in yellow.
    public string SelectedKind { get; set; } = "";

    readonly TableView table;
    Zone? dragging;
    Vector2 dragOffset;
    Vector2 dragCenter;
    Vector2 pressAt;

    public ZoneDragger(TableView tableToEdit)
    {
        table = tableToEdit;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 20;
        // Clicks go to the last child, whatever its z_index, so stay in front of card views as they're made.
        table.ChildEnteredTree += _ => CallDeferred(CanvasItem.MethodName.MoveToFront);
    }

    public override void _Draw()
    {
        if (table.Game == null)
        {
            return;
        }
        Font font = GetThemeDefaultFont();
        // The editor shrinks the table to fit; names stay readable at their usual size.
        int fontSize = Mathf.RoundToInt(FontSize / GetGlobalTransform().Scale.X);
        foreach (Zone zone in table.Game.Zones.Values)
        {
            Rect2 rect = table.SlotRect(zone);
            if (zone == dragging)
            {
                rect.Position = dragCenter - rect.Size / 2;
            }
            bool selected = zone.Kind == SelectedKind;
            DrawRect(rect, selected ? SelectedColor : OutlineColor, false, selected ? 3 : 1.5f);
            DrawString(font, rect.Position + new Vector2(2, -4), zone.Name, HorizontalAlignment.Left, -1, fontSize, NameColor);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed)
            {
                dragging = ZoneAt(button.Position);
                pressAt = button.Position;
                if (dragging != null)
                {
                    Vector2 center = table.SlotRect(dragging).GetCenter();
                    dragOffset = button.Position - center;
                    dragCenter = center;
                    ZonePicked?.Invoke(dragging);
                }
            }
            else if (dragging != null)
            {
                if (button.Position.DistanceTo(pressAt) > DragThreshold)
                {
                    ZoneMoved?.Invoke(dragging, dragCenter / table.Size);
                }
                dragging = null;
                QueueRedraw();
            }
        }
        else if (@event is InputEventMouseMotion motion && dragging != null)
        {
            dragCenter = motion.Position - dragOffset;
            QueueRedraw();
        }
    }

    /// The smallest zone outline under the point (so a zone sitting inside a bigger one can still be picked).
    Zone? ZoneAt(Vector2 point)
    {
        Zone? found = null;
        float foundArea = float.PositiveInfinity;
        if (table.Game == null)
        {
            return null;
        }
        foreach (Zone zone in table.Game.Zones.Values)
        {
            Rect2 rect = table.SlotRect(zone);
            if (rect.HasPoint(point) && rect.Area < foundArea)
            {
                found = zone;
                foundArea = rect.Area;
            }
        }
        return found;
    }
}

using Core;
using Godot;

/// The table: one CardView per card, tweened to its place from ZoneLayout. Cards are dragged to the nearest zone,
/// and every click, drag and reorder goes to Submit as an input.
public partial class TableView : Control
{
    public static readonly Color FeltColor = new(0.12f, 0.38f, 0.22f);
    static readonly Color LabelColor = new(1, 1, 1, 0.7f);
    static readonly Color ZoneLabelColor = new(1, 1, 1, 0.45f);
    static readonly Color SlotBorder = new(1, 1, 1, 0.25f);
    static readonly Color CurrentSlotBorder = new(1, 0.85f, 0.4f, 0.8f);
    const int LabelFontSize = 14;
    const float LabelGap = 22;
    const float BottomMargin = 8;
    const float ZoomGap = 12;
    const float LabelOverhang = 20;
    static readonly Vector2 SeatPadding = new(10, 4);
    static readonly Color SeatTextColor = Colors.White;
    static readonly Color CurrentSeatTextColor = new(0.1f, 0.1f, 0.1f);

    static readonly StyleBoxFlat slotStyle = SlotBox(SlotBorder);
    static readonly StyleBoxFlat currentSlotStyle = SlotBox(CurrentSlotBorder);
    static readonly StyleBoxFlat seatStyle = SeatBox(new(0, 0, 0, 0.45f));
    static readonly StyleBoxFlat currentSeatStyle = SeatBox(CurrentSlotBorder);

    /// Where inputs go: the game itself, or the online session (set by Main).
    public Func<PlayerInput, bool> Submit { get; set; } = _ => false;
    public bool ShowZoneNames { get; set; }
    public Game? Game => game;

    Game? game;
    readonly Dictionary<Card, CardView> views = [];
    Zone? previewZone;
    CardView? zoomView;
    Zone? hoverZone;
    bool hoverAllowed;
    readonly List<CardView> carriedViews = [];
    readonly List<Vector2> carriedOffsets = [];

    public override void _Ready()
    {
        Resized += LayoutAll;
    }

    public void ShowGame(Game gameToShow)
    {
        foreach (CardView view in views.Values)
        {
            view.QueueFree();
        }
        views.Clear();
        game = gameToShow;
        MakeZoomView();
        foreach (Zone zone in game.Zones.Values)
        {
            zone.Changed += changed =>
            {
                LayoutZone(changed);
                QueueRedraw();
            };
        }
        game.VariablesChanged += () =>
        {
            QueueRedraw();
            RefreshViews();
        };
        game.StateChanged += RefreshViews;
        LayoutAll();
        QueueRedraw();
    }

    void LayoutAll()
    {
        if (game == null)
        {
            return;
        }
        foreach (Zone zone in game.Zones.Values)
        {
            LayoutZone(zone);
        }
    }

    /// With an incoming card, the other cards make room for it at that index (the incoming card itself is being dragged).
    void LayoutZone(Zone zone, Card? incoming = null, int index = 0)
    {
        Vector2 center = AnchorOf(zone) * Size;
        float turn = Mathf.DegToRad((float)RotationOf(zone));
        List<List<Card>> groups = Groups(zone);
        if (incoming != null)
        {
            List<Card> arranged = zone.Cards.Where(card => card != incoming).ToList();
            arranged.Insert(index, incoming);
            groups = [arranged];
        }
        List<float> groupCenters = ZoneLayout.GroupCenters(groups.Select(group => group.Count).ToList());
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            List<Card> group = groups[groupIndex];
            List<float> columnOffsets = zone.Layout == "column" ? ZoneLayout.ColumnOffsets(group.Select(game!.ShowsFace).ToList(), MaxSpread(zone)) : [];
            for (int i = 0; i < group.Count; i++)
            {
                if (group[i] == incoming)
                {
                    continue;
                }
                CardView view = ViewFor(group[i], center);
                view.FaceUp = game!.ShowsFace(group[i]);
                MoveChild(view, -1);
                Transform2D placement = zone.Layout == "column"
                    ? new(0, new Vector2(0, columnOffsets[i]))
                    : ZoneLayout.Place(zone.Layout, i, group.Count, MaxSpread(zone), zone.Spread);
                Vector2 offset = (new Vector2(groupCenters[groupIndex], 0) + placement.Origin).Rotated(turn);
                view.MoveTo(center + offset - CardView.CardSize / 2, placement.Rotation + turn);
            }
        }
    }

    List<List<Card>> Groups(Zone zone)
    {
        if (zone.Layout != "melds")
        {
            return [zone.Cards];
        }
        List<List<Card>> groups = game!.MeldRules.Best(zone.Cards).Select(meld => ((List<object?>)meld!).Cast<Card>().ToList()).ToList();
        List<Card> deadwood = game.MeldRules.Deadwood(zone.Cards);
        if (deadwood.Count > 0)
        {
            groups.Add(deadwood);
        }
        return groups;
    }

    void RefreshViews()
    {
        QueueRedraw();
        foreach ((Card card, CardView view) in views)
        {
            view.FaceUp = game!.ShowsFace(card);
            view.Highlighted = game.Choice.Options.Contains(card) && game.IsLocalTurn();
            view.Selected = game.Choice.Selected.Contains(card);
            view.Movable = game.MayHandle(card);
            view.MarksText = string.Join(" ", card.Marks.Values.Select(Values.Format));
        }
    }

    CardView ViewFor(Card card, Vector2 spawnCenter)
    {
        if (views.TryGetValue(card, out CardView? existing))
        {
            return existing;
        }
        CardView view = new(card, game!.Rules.CardFace) { Position = spawnCenter - CardView.CardSize / 2 };
        view.PickedUp += OnCardPickedUp;
        view.Dragged += OnCardDragged;
        view.Clicked += clicked => Press(clicked, "click");
        // Two quick clicks on a zone with no double-click steps (like a solitaire stock) are two clicks, not one double-click.
        view.DoubleClicked += clicked => Press(clicked, game!.Plays.HasDoubleClick(game.ZoneOf(clicked.Card!)) ? "double_click" : "click");
        view.Hovered += OnCardHovered;
        view.Unhovered += _ => HideZoom();
        view.Dropped += OnCardDropped;
        AddChild(view);
        views[card] = view;
        return view;
    }

    /// With "zoom" in card_face, hovering a face-up card shows an enlarged copy beside it.
    void MakeZoomView()
    {
        zoomView?.QueueFree();
        zoomView = null;
        if (!game!.Rules.CardFace.ContainsKey("zoom") || game.Zones.Count == 0)
        {
            return;
        }
        zoomView = new(null, game.Rules.CardFace)
        {
            PivotOffset = Vector2.Zero, MouseFilter = MouseFilterEnum.Ignore, ZIndex = 10, Visible = false,
        };
        AddChild(zoomView);
    }

    void OnCardHovered(CardView view)
    {
        if (zoomView == null || !view.FaceUp || view.Grabbed)
        {
            return;
        }
        float zoom = (float)Values.ToDouble(game!.Rules.CardFace["zoom"]);
        Vector2 zoomSize = CardView.CardSize * zoom;
        Vector2 spot = view.Position + new Vector2(CardView.CardSize.X + ZoomGap, (CardView.CardSize.Y - zoomSize.Y) / 2);
        if (spot.X + zoomSize.X > Size.X)
        {
            spot.X = view.Position.X - zoomSize.X - ZoomGap;
        }
        zoomView.Card = view.Card;
        zoomView.Scale = new Vector2(zoom, zoom);
        zoomView.Position = spot.Clamp(Vector2.Zero, Size - zoomSize);
        zoomView.Visible = true;
        zoomView.QueueRedraw();
    }

    void HideZoom()
    {
        if (zoomView != null)
        {
            zoomView.Visible = false;
        }
    }

    void OnCardPickedUp(CardView view)
    {
        HideZoom();
        carriedViews.Clear();
        carriedOffsets.Clear();
        foreach (Card card in game!.Carried(view.Card!).Skip(1))
        {
            CardView carriedView = views[card];
            carriedView.StopMoving();
            MoveChild(carriedView, -1);
            carriedViews.Add(carriedView);
            carriedOffsets.Add(carriedView.Position - view.Position);
        }
    }

    void OnCardDragged(CardView view)
    {
        for (int i = 0; i < carriedViews.Count; i++)
        {
            carriedViews[i].Position = view.Position + carriedOffsets[i];
        }
        Zone source = game!.ZoneOf(view.Card!);
        Zone? target = NearestZone(GetLocalMousePosition(), DraggedViews(view));
        ShowDropHint(view, target == source ? null : target);
        if (target != null && (!MakesRoom(target) || target != source && !hoverAllowed))
        {
            target = null;
        }
        if (previewZone != null && previewZone != target)
        {
            CloseGap(view.Card!);
        }
        previewZone = target;
        if (target != null)
        {
            LayoutZone(target, view.Card, DropIndex(target, view.Card!, GetLocalMousePosition()));
            foreach (CardView draggedView in DraggedViews(view))
            {
                MoveChild(draggedView, -1);
            }
        }
    }

    /// Outlines the zone the card would land in, and the card itself, green if the move is allowed or red if not.
    void ShowDropHint(CardView view, Zone? target)
    {
        if (target != hoverZone)
        {
            hoverZone = target;
            hoverAllowed = target != null && game!.Plays.CanMove(view.Card!, target);
            QueueRedraw();
        }
        view.Hint = hoverZone == null ? CardView.DropHint.None : hoverAllowed ? CardView.DropHint.Allowed : CardView.DropHint.Refused;
    }

    /// Back to the normal layout, except that a card still being dragged stays under the cursor.
    void CloseGap(Card draggedCard)
    {
        if (previewZone!.Cards.Contains(draggedCard))
        {
            LayoutZone(previewZone, draggedCard, previewZone.Cards.IndexOf(draggedCard));
        }
        else
        {
            LayoutZone(previewZone);
        }
    }

    static bool MakesRoom(Zone zone) => zone.Layout is "row" or "fan";

    int DropIndex(Zone zone, Card card, Vector2 point)
    {
        if (zone.SortColumns.Count > 0 && !zone.Cards.Contains(card))
        {
            return zone.SortedIndex(card);
        }
        int slots = zone.Cards.Contains(card) ? zone.Cards.Count : zone.Cards.Count + 1;
        return NearestSlot(zone, point, slots);
    }

    List<CardView> DraggedViews(CardView view) => [view, .. carriedViews];

    /// A click ends the little pick-up that every press starts, then goes to the game.
    void Press(CardView view, string inputType)
    {
        carriedViews.Clear();
        ShowDropHint(view, null);
        if (previewZone != null)
        {
            LayoutZone(previewZone);
            previewZone = null;
        }
        Zone zone = game!.ZoneOf(view.Card!);
        Submit(new(inputType) { Card = view.Card!.Id });
        LayoutZone(zone);
    }

    void OnCardDropped(CardView view)
    {
        Zone source = game!.ZoneOf(view.Card!);
        Vector2 dropPoint = GetLocalMousePosition();
        Zone destination = NearestZone(dropPoint, DraggedViews(view))!;
        int index = MakesRoom(destination) ? DropIndex(destination, view.Card!, dropPoint) : -1;
        carriedViews.Clear();
        previewZone = null;
        ShowDropHint(view, null);
        if (destination == source && MakesRoom(source))
        {
            if (!Submit(new("reorder") { Card = view.Card!.Id, Index = index }))
            {
                LayoutZone(source);
            }
        }
        else if (destination == source || !Submit(new("drag") { Card = view.Card!.Id, To = destination.Name, Index = index }))
        {
            LayoutZone(source);
            LayoutZone(destination);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (game != null && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button)
        {
            foreach (Zone zone in game.Zones.Values.Where(zone => zone.IsEmpty && SlotRect(zone).HasPoint(button.Position)))
            {
                Submit(new("click_empty") { Zone = zone.Name });
            }
        }
    }

    int NearestSlot(Zone zone, Vector2 point, int slots)
    {
        float offset = (point - AnchorOf(zone) * Size).Rotated(-Mathf.DegToRad((float)RotationOf(zone))).X;
        int nearest = 0;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < slots; i++)
        {
            float distance = Mathf.Abs(ZoneLayout.Place(zone.Layout, i, slots, MaxSpread(zone)).Origin.X - offset);
            if (distance < nearestDistance)
            {
                nearest = i;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    float MaxSpread(Zone zone)
    {
        if (zone.Layout == "column")
        {
            return Size.Y * (1 - AnchorOf(zone).Y) - CardView.CardSize.Y / 2 - BottomMargin;
        }
        return zone.Width > 0 ? (float)zone.Width * (IsSideways(zone) ? Size.Y : Size.X) : ZoneLayout.MaxWidth;
    }

    bool IsSideways(Zone zone) => Mathf.IsEqualApprox(Mathf.Abs(Mathf.Sin(Mathf.DegToRad((float)RotationOf(zone)))), 1);

    Zone? NearestZone(Vector2 point, List<CardView> ignoredViews)
    {
        Zone? nearest = null;
        float nearestDistance = float.PositiveInfinity;
        foreach (Zone zone in game!.Zones.Values)
        {
            List<Vector2> centers = [AnchorOf(zone) * Size, .. zone.Cards.Where(card => !ignoredViews.Contains(views[card])).Select(card => views[card].Position + CardView.CardSize / 2)];
            foreach (Vector2 center in centers.Where(center => point.DistanceTo(center) < nearestDistance))
            {
                nearest = zone;
                nearestDistance = point.DistanceTo(center);
            }
        }
        return nearest;
    }

    /// Online, each screen puts its own seat at the bottom, where seat 1 is in the game file: every player's
    /// zones and name tag move round the table by the same number of seats. Shared zones stay put.
    int SeatOnScreen(int player)
    {
        if (game!.LocalSeat == 0 || player == 0 || game.LocalSeat > game.Rules.Players)
        {
            return player;
        }
        return Mathf.PosMod(player - game.LocalSeat, game.Rules.Players) + 1;
    }

    /// The zone whose place on the table this one takes on this screen.
    Zone PlaceOf(Zone zone)
    {
        int seat = SeatOnScreen(zone.Owner);
        return seat != zone.Owner ? game!.Zones.GetValueOrDefault($"{zone.Kind}_{seat}", zone) : zone;
    }

    Vector2 AnchorOf(Zone zone) => ToGodot(PlaceOf(zone).Anchor);

    double RotationOf(Zone zone) => PlaceOf(zone).Rotation;

    static Vector2 ToGodot(System.Numerics.Vector2 vector) => new(vector.X, vector.Y);

    /// Where a seat's name tag goes: from "seats", or round the table from seat 1's when only that is given.
    Vector2 SeatSpot(int seat)
    {
        List<object?> seats = game!.Rules.Seats;
        List<object?> spot = (List<object?>)seats[seats.Count == 1 ? 0 : seat - 1]!;
        System.Numerics.Vector2 given = new((float)Values.ToDouble(spot[0]), (float)Values.ToDouble(spot[1]));
        return ToGodot(seats.Count == 1 ? Zone.AroundTable(given, seat, game.Rules.Players) : given);
    }

    /// Where a zone sits on the table, as the outline drawn for it (also used by the editor to drag zones).
    public Rect2 SlotRect(Zone zone)
    {
        Vector2 slotSize = IsSideways(zone) ? new Vector2(CardView.CardSize.Y, CardView.CardSize.X) : CardView.CardSize;
        return new(AnchorOf(zone) * Size - slotSize / 2, slotSize);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), FeltColor);
        if (game == null)
        {
            return;
        }
        Font font = GetThemeDefaultFont();
        foreach (Zone zone in game.Zones.Values)
        {
            if (!zone.HasOutline || zone.Layout == "melds" && zone.IsEmpty)
            {
                continue;
            }
            Rect2 slot = SlotRect(zone);
            bool isCurrent = game.Rules.Players > 1 && zone.Owner == game.CurrentPlayer();
            DrawStyleBox(isCurrent ? currentSlotStyle : slotStyle, slot);
            if (zone.Label.Length > 0)
            {
                Vector2 baseline = new(slot.Position.X, slot.GetCenter().Y + LabelFontSize / 3f);
                DrawString(font, baseline, zone.Label, HorizontalAlignment.Center, slot.Size.X, LabelFontSize, ZoneLabelColor);
            }
            if (ShowZoneNames)
            {
                Vector2 namePosition = slot.Position - new Vector2(LabelOverhang, LabelGap);
                DrawString(font, namePosition, $"{zone.Name} ({zone.Cards.Count})", HorizontalAlignment.Center, slot.Size.X + LabelOverhang * 2, LabelFontSize, LabelColor);
            }
        }
        if (game.IsLocalTurn())
        {
            foreach (Zone zone in game.Choice.Options.OfType<Zone>())
            {
                DrawStyleBox(CardView.HighlightStyle, SlotRect(zone).Grow(CardView.HighlightWidth / 2f));
            }
        }
        if (hoverZone != null)
        {
            DrawStyleBox(hoverAllowed ? CardView.AllowedStyle : CardView.RefusedStyle, SlotRect(hoverZone).Grow(CardView.HighlightWidth / 2f));
        }
        DrawSeats(font);
    }

    void DrawSeats(Font font)
    {
        int lastSeat = game!.Rules.Seats.Count > 0 ? game.Rules.Players : 0;
        for (int player = 1; player <= lastSeat; player++)
        {
            bool isCurrent = player == game.CurrentPlayer();
            string text = game.PlayerName(player);
            Vector2 textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, LabelFontSize);
            Vector2 seat = SeatSpot(SeatOnScreen(player)) * Size;
            Rect2 tag = new(seat - textSize / 2 - SeatPadding, textSize + SeatPadding * 2);
            DrawStyleBox(isCurrent ? currentSeatStyle : seatStyle, tag);
            Vector2 baseline = new(tag.Position.X + SeatPadding.X, tag.Position.Y + SeatPadding.Y + font.GetAscent(LabelFontSize));
            DrawString(font, baseline, text, HorizontalAlignment.Left, -1, LabelFontSize, isCurrent ? CurrentSeatTextColor : SeatTextColor);
        }
    }

    static StyleBoxFlat SeatBox(Color color)
    {
        StyleBoxFlat box = new() { BgColor = color };
        box.SetCornerRadiusAll(10);
        return box;
    }

    static StyleBoxFlat SlotBox(Color borderColor)
    {
        StyleBoxFlat box = new() { BgColor = new(0, 0, 0, 0.15f), BorderColor = borderColor };
        box.SetBorderWidthAll(2);
        box.SetCornerRadiusAll(8);
        return box;
    }
}

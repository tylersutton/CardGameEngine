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
    static readonly Color SpaceColor = new(0, 0, 0, 0.15f);
    const float SpaceBorderWidth = 1.5f;
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
    /// Designer > Show plays: outline everything this screen's player could do something with now, to check the rules.
    public bool ShowPlays { get; set; }
    public Game? Game => game;
    public IReadOnlyDictionary<Card, CardView> CardViews => views;

    Game? game;
    ImageCache images = new(ProjectFiles.With());
    readonly Dictionary<Card, CardView> views = [];
    Zone? previewZone;
    CardView? zoomView;
    Zone? hoverZone;
    bool hoverAllowed;
    /// The cards and empty zones outlined with ShowPlays (Plays.Playable).
    HashSet<object> outlined = [];
    /// The cards in this screen's player's own zones that they can't play now, in the zones something plays from now
    /// (Plays.PlaysFrom): the cards that don't follow suit, or a whole hand when nothing in it beats the table. A zone
    /// nothing plays from now (a hand while drawing or bidding) isn't dimmed: it would say nothing.
    HashSet<Card> dimmed = [];
    /// Each card's zone before the input being carried out, to see which cards it moves (see ShowMoved).
    readonly Dictionary<Card, string> zonesBefore = [];
    readonly List<CardView> carriedViews = [];
    readonly List<Vector2> carriedOffsets = [];

    public override void _Ready()
    {
        Resized += LayoutAll;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _ExitTree()
    {
        ForgetInputs();
    }

    public void ShowGame(Game gameToShow)
    {
        foreach (CardView view in views.Values)
        {
            view.QueueFree();
        }
        views.Clear();
        ForgetInputs();
        game = gameToShow;
        game.InputApplying += RememberZones;
        game.InputApplied += ShowMoved;
        images = new(game.Files);
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
        game.StateChanged += () =>
        {
            FindPlayable();
            RefreshViews();
        };
        LayoutAll();
        QueueRedraw();
    }

    void ForgetInputs()
    {
        if (game != null)
        {
            game.InputApplying -= RememberZones;
            game.InputApplied -= ShowMoved;
        }
    }

    void RememberZones(PlayerInput input)
    {
        foreach (Card card in game!.Cards)
        {
            zonesBefore[card] = card.ZoneName;
        }
    }

    /// The cards someone else's input moved (a bot's, or another player's online) glow for a moment, to show what
    /// changed.
    void ShowMoved(PlayerInput input)
    {
        bool madeElsewhere = input.Player is int player && (game!.IsBot(player) || game.LocalSeat != 0 && player != game.LocalSeat);
        if (!madeElsewhere || input.Type == "undo")
        {
            return;
        }
        foreach (Card card in game!.Cards.Where(card => card.ZoneName != zonesBefore.GetValueOrDefault(card) && views.ContainsKey(card)))
        {
            views[card].Flash();
        }
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
        Vector2 center = CenterOf(zone);
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
                view.FaceUp = ShowsFace(group[i]);
                MoveChild(view, -1);
                Transform2D placement = zone.Layout == "column"
                    ? new(0, new Vector2(0, columnOffsets[i]))
                    : zone.Board != null && zone.Layout == "pile"
                    ? ZoneLayout.Cluster(i, group.Count, zone.Board.SpaceSize)
                    : ZoneLayout.Place(zone.Layout, i, group.Count, MaxSpread(zone), zone.Spread);
                Vector2 offset = (new Vector2(groupCenters[groupIndex], 0) + placement.Origin).Rotated(turn);
                view.MoveTo(center + offset - view.Size / 2, placement.Rotation + turn);
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

    /// Plays are pointed out only on this screen's turn and while the rules hold. While choosing (like picking the
    /// cards for President's Play), the plays are the choice's options, which are outlined already.
    void FindPlayable()
    {
        bool pointsOut = game!.IsLocalTurn() && !game.Sandbox && !game.Over;
        HashSet<object> playable = !pointsOut ? [] : game.Choice.IsOpen ? [.. game.Choice.Options.OfType<Card>()] : game.Plays.Playable();
        outlined = ShowPlays && !game.Choice.IsOpen ? playable : [];
        dimmed = [];
        foreach (Zone zone in game.Zones.Values.Where(zone => pointsOut && zone.Owner == game.Viewer() && PlaysFrom(zone)))
        {
            dimmed.UnionWith(Plays.ReachableCards(zone).Where(card => !playable.Contains(card)));
        }
    }

    /// While choosing, the zones holding the options; otherwise the zones the actions that may be done now play from.
    bool PlaysFrom(Zone zone) =>
        game!.Choice.IsOpen ? game.Choice.Options.Any(option => option is Card card && game.ZoneOf(card) == zone) : game.Plays.PlaysFrom(zone);

    /// Shows or hides the plays (see ShowPlays) at once, without waiting for the next change.
    public void RefreshPlays()
    {
        if (game != null)
        {
            FindPlayable();
            RefreshViews();
        }
    }

    void RefreshViews()
    {
        QueueRedraw();
        foreach ((Card card, CardView view) in views)
        {
            view.FaceUp = ShowsFace(card);
            view.Highlighted = game!.Choice.Options.Contains(card) && game.IsLocalTurn();
            view.Playable = outlined.Contains(card);
            view.Dimmed = dimmed.Contains(card);
            view.Selected = game.Choice.Selected.Contains(card);
            view.Movable = game.MayHandle(card);
            view.MarksText = string.Join(" ", card.Marks.Values.Select(Values.Format));
        }
    }

    /// Whether the card is drawn face up: if this screen can see it and, in a pile, it's on top (or one of the top
    /// cards the pile spreads), as the cards under it are hidden by it.
    bool ShowsFace(Card card)
    {
        Zone zone = game!.ZoneOf(card);
        bool underTop = zone.Layout == "pile" && zone.Board == null && zone.Cards.IndexOf(card) < zone.Cards.Count - Mathf.Max(zone.Spread, 1);
        return game.ShowsFace(card) && !underTop;
    }

    CardView ViewFor(Card card, Vector2 spawnCenter)
    {
        if (views.TryGetValue(card, out CardView? existing))
        {
            return existing;
        }
        CardView view = new(card, game!.Rules.CardFace, images);
        view.Position = spawnCenter - view.Size / 2;
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
        zoomView = new(null, game.Rules.CardFace, images)
        {
            PivotOffset = Vector2.Zero, MouseFilter = MouseFilterEnum.Ignore, ZIndex = 10, Visible = false,
        };
        AddChild(zoomView);
    }

    void OnCardHovered(CardView view)
    {
        if (zoomView == null || !view.FaceUp || view.Grabbed || view.IsPiece)
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
            hoverAllowed = target != null && game!.MayAct(game.Viewer(), PlayerInput.Drag(view.Card!, target)) && game.Plays.CanMove(view.Card!, target);
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
            if (index == source.Cards.IndexOf(view.Card!) || !Submit(new("reorder") { Card = view.Card!.Id, Index = index }))
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
        float offset = (point - CenterOf(zone)).Rotated(-Mathf.DegToRad((float)RotationOf(zone))).X;
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
            List<Vector2> centers = [CenterOf(zone), .. zone.Cards.Where(card => !ignoredViews.Contains(views[card])).Select(card => views[card].Position + views[card].Size / 2)];
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

    /// Where the zone's cards gather: its anchor on the table, moved to its own space if it's a board space.
    Vector2 CenterOf(Zone zone) => AnchorOf(zone) * Size + ToGodot(PlaceOf(zone).Offset);

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
    /// A board space's is its square, or on a hex board the middle band of its hexagon, so neighbours don't overlap.
    public Rect2 SlotRect(Zone zone)
    {
        Vector2 slotSize = IsSideways(zone) ? new Vector2(CardView.CardSize.Y, CardView.CardSize.X) : CardView.CardSize;
        if (zone.Board is { } board)
        {
            slotSize = new(board.SpaceSize, board.Shape == Board.Hex ? board.SpaceSize * Mathf.Sqrt(3) / 2 : board.SpaceSize);
        }
        return new(CenterOf(zone) - slotSize / 2, slotSize);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), FeltColor);
        if (game == null)
        {
            return;
        }
        Font font = GetThemeDefaultFont();
        DrawBoardImages();
        foreach (Zone zone in game.Zones.Values)
        {
            if (!zone.HasOutline || zone.Layout == "melds" && zone.IsEmpty)
            {
                continue;
            }
            Rect2 slot = SlotRect(zone);
            bool isCurrent = game.Rules.Players > 1 && zone.Owner == game.CurrentPlayer();
            if (zone.Board != null)
            {
                DrawSpace(zone, slot);
            }
            else
            {
                DrawStyleBox(isCurrent ? currentSlotStyle : slotStyle, slot);
            }
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
        foreach (Zone zone in game.Zones.Values.Where(zone => zone.ShowsCount))
        {
            // Board spaces touch, so a space's count goes in its corner rather than under it.
            Rect2 slot = SlotRect(zone);
            string count = zone.Cards.Count.ToString();
            if (zone.Board != null)
            {
                DrawString(font, slot.End - new Vector2(slot.Size.X + 4, 4), count, HorizontalAlignment.Right, slot.Size.X, LabelFontSize, LabelColor);
            }
            else
            {
                DrawString(font, new Vector2(slot.Position.X, slot.End.Y + LabelFontSize + 2), count, HorizontalAlignment.Center, slot.Size.X, LabelFontSize, LabelColor);
            }
        }
        foreach (Zone zone in outlined.OfType<Zone>())
        {
            DrawHighlight(zone, CardView.PlayableStyle);
        }
        if (game.IsLocalTurn())
        {
            foreach (Zone zone in game.Choice.Options.OfType<Zone>())
            {
                DrawHighlight(zone, CardView.HighlightStyle);
            }
        }
        if (hoverZone != null)
        {
            DrawHighlight(hoverZone, hoverAllowed ? CardView.AllowedStyle : CardView.RefusedStyle);
        }
        DrawSeats(font);
    }

    /// Board spaces touch, so they're drawn without the rounded corners of card slots: squares, or pointy-topped
    /// hexagons, in the board's paint where it has some.
    void DrawSpace(Zone zone, Rect2 slot)
    {
        Color fill = Color.FromString(game!.PaintOf(zone), SpaceColor);
        if (zone.Board!.Shape != Board.Hex)
        {
            DrawRect(slot, fill);
            DrawRect(slot, SlotBorder, false, SpaceBorderWidth);
            return;
        }
        Vector2[] corners = HexCorners(zone, 0);
        DrawColoredPolygon(corners[..6], fill);
        DrawPolyline(corners, SlotBorder, SpaceBorderWidth, true);
    }

    /// Each board's picture, stretched over all its spaces, under them.
    void DrawBoardImages()
    {
        foreach (Zone firstSpace in game!.Zones.Values.Where(zone => zone.Number == 1 && zone.Board is { Image.Length: > 0 }))
        {
            if (images.Get(firstSpace.Board!.Image) is { } image)
            {
                Rect2 area = game.ZoneGroups[firstSpace.Kind].Select(SpaceArea).Aggregate((all, space) => all.Merge(space));
                DrawTextureRect(image, area, false);
            }
        }
    }

    /// The whole of a board space: its square, or its hexagon from point to point.
    Rect2 SpaceArea(Zone space)
    {
        float size = space.Board!.SpaceSize;
        Vector2 extent = new(size, space.Board.Shape == Board.Hex ? size * 2 / Mathf.Sqrt(3) : size);
        return new(CenterOf(space) - extent / 2, extent);
    }

    /// A hex space's corners, going round back to the first (for an outline), grown outward by that many pixels.
    Vector2[] HexCorners(Zone zone, float grow)
    {
        float radius = zone.Board!.SpaceSize / Mathf.Sqrt(3) + grow;
        Vector2 center = CenterOf(zone);
        return Enumerable.Range(0, 7).Select(corner => center + Vector2.FromAngle(Mathf.DegToRad(60 * corner - 90)) * radius).ToArray();
    }

    /// An outline round a zone to choose or drop on: the zone's slot, or a hex space's own hexagon.
    void DrawHighlight(Zone zone, StyleBoxFlat style)
    {
        if (zone.Board?.Shape == Board.Hex)
        {
            DrawPolyline(HexCorners(zone, CardView.HighlightWidth / 2f), style.BorderColor, style.BorderWidthTop, true);
            return;
        }
        DrawStyleBox(style, SlotRect(zone).Grow(CardView.HighlightWidth / 2f));
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

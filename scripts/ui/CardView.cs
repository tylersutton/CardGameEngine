using Core;
using Godot;

/// One card on the table: drawn from its deck row and the game's "card_face" (as a card, or as a piece: a disc or a
/// square), picked up and dropped with the mouse, and tweened to wherever TableView places it.
public partial class CardView : Control
{
    public static readonly Vector2 CardSize = new(100, 140);
    const float DefaultPieceSize = 50;
    const string Disc = "disc";
    const string SquarePiece = "square";
    const float MoveTime = 0.25f;
    const float HoverLift = 16;
    const float ClickDistance = 5;
    const float Margin = 8;
    const int CornerFontSize = 14;
    const int TitleFontSize = 24;
    const int BodyFontSize = 12;
    const int TextFontSize = 10;
    const int MinTextFontSize = 6;
    const int MinTitleFontSize = 12;
    public const int HighlightWidth = 4;
    const float MarksHeight = 18;
    const int MarksFontSize = 13;
    static readonly Color HighlightColor = new(1, 0.85f, 0.2f);
    static readonly Color SelectedColor = new(0.3f, 0.75f, 1);
    static readonly Color AllowedColor = new(0.35f, 0.9f, 0.45f);
    static readonly Color RefusedColor = new(0.95f, 0.35f, 0.3f);
    static readonly Color MarksBackground = new(0.1f, 0.1f, 0.1f, 0.8f);
    static readonly Color FaceBorder = new(0.75f, 0.75f, 0.75f);
    static readonly Color PieceBorder = new(0, 0, 0, 0.45f);
    static readonly Color PieceShadow = new(0, 0, 0, 0.3f);
    static readonly Color HiddenPieceColor = new(0.16f, 0.24f, 0.52f);

    public enum DropHint { None, Allowed, Refused }

    static readonly StyleBoxFlat faceStyle = RoundedBox(Colors.White, FaceBorder);
    /// One face box per background color, shared by every card with that color.
    static readonly Dictionary<Color, StyleBoxFlat> faceStylesByColor = [];
    static readonly Dictionary<Color, StyleBoxFlat> pieceStylesByColor = [];
    static readonly StyleBoxFlat backStyle = RoundedBox(new(0.16f, 0.24f, 0.52f), Colors.White);
    public static readonly StyleBoxFlat HighlightStyle = OutlineBox(HighlightColor);
    static readonly StyleBoxFlat selectedStyle = OutlineBox(SelectedColor);
    public static readonly StyleBoxFlat AllowedStyle = OutlineBox(AllowedColor);
    public static readonly StyleBoxFlat RefusedStyle = OutlineBox(RefusedColor);

    public event Action<CardView>? PickedUp;
    public event Action<CardView>? Dragged;
    public event Action<CardView>? Clicked;
    public event Action<CardView>? DoubleClicked;
    public event Action<CardView>? Dropped;
    public event Action<CardView>? Hovered;
    public event Action<CardView>? Unhovered;

    public Card? Card { get; set; }
    Dictionary<string, object?> face = [];
    Tween? tween;
    Vector2 grabStart;
    Vector2 grabOffset;
    /// The press was the second of a double-click; it counts as one when the button comes up, like any click.
    bool pressedTwice;
    bool faceUp = true;
    bool highlighted;
    bool selected;
    DropHint hint;
    string marksText = "";
    float lift;

    /// Off for cards this screen's player can't move (another player's, online): they can still be clicked.
    public bool Movable { get; set; } = true;
    public bool Grabbed { get; private set; }

    public bool FaceUp
    {
        get => faceUp;
        set
        {
            faceUp = value;
            QueueRedraw();
        }
    }

    public bool Highlighted
    {
        get => highlighted;
        set
        {
            highlighted = value;
            QueueRedraw();
        }
    }

    public bool Selected
    {
        get => selected;
        set
        {
            selected = value;
            QueueRedraw();
        }
    }

    public DropHint Hint
    {
        get => hint;
        set
        {
            hint = value;
            QueueRedraw();
        }
    }

    public string MarksText
    {
        get => marksText;
        set
        {
            marksText = value;
            QueueRedraw();
        }
    }

    float Lift
    {
        get => lift;
        set
        {
            lift = value;
            QueueRedraw();
        }
    }

    public CardView()
    {
    }

    public CardView(Card? card, Dictionary<string, object?> cardFace)
    {
        Card = card;
        face = cardFace;
        Size = IsPiece ? Vector2.One * PieceSize() : CardSize;
        PivotOffset = Size / 2;
        MouseEntered += () =>
        {
            LiftTo(HoverLift);
            Hovered?.Invoke(this);
        };
        MouseExited += () =>
        {
            LiftTo(0);
            Unhovered?.Invoke(this);
        };
    }

    public void MoveTo(Vector2 targetPosition, float targetRotation)
    {
        StopMoving();
        tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(this, "position", targetPosition, MoveTime);
        tween.TweenProperty(this, "rotation", targetRotation, MoveTime);
    }

    public void StopMoving()
    {
        tween?.Kill();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed)
            {
                pressedTwice = button.DoubleClick;
                Grab();
            }
            else if (Grabbed)
            {
                Release();
            }
        }
        else if (@event is InputEventMouseMotion && Grabbed && Movable)
        {
            GlobalPosition = GetGlobalMousePosition() - grabOffset;
            Dragged?.Invoke(this);
        }
    }

    void Grab()
    {
        Grabbed = true;
        grabStart = GetGlobalMousePosition();
        grabOffset = grabStart - GlobalPosition;
        if (Movable)
        {
            StopMoving();
            GetParent().MoveChild(this, -1);
            PickedUp?.Invoke(this);
        }
    }

    void Release()
    {
        Grabbed = false;
        if (GetGlobalMousePosition().DistanceTo(grabStart) < ClickDistance)
        {
            (pressedTwice ? DoubleClicked : Clicked)?.Invoke(this);
        }
        else if (Movable)
        {
            Dropped?.Invoke(this);
        }
    }

    void LiftTo(float height)
    {
        CreateTween().TweenMethod(Callable.From<float>(value => Lift = value), Lift, height, 0.1f);
    }

    public override void _Draw()
    {
        DrawSetTransform(new Vector2(0, -Lift));
        Rect2 rect = new(Vector2.Zero, Size);
        if (IsPiece)
        {
            DrawPiece(rect);
        }
        else if (FaceUp)
        {
            DrawFace(rect);
        }
        else
        {
            DrawStyleBox(backStyle, rect);
        }
        if (MarksText.Length > 0)
        {
            DrawMarks(rect);
        }
        if (OutlineStyle() is { } outline && Shape() == Disc)
        {
            DrawArc(rect.GetCenter(), rect.Size.X / 2 + HighlightWidth / 2f, 0, Mathf.Tau, 48, outline.BorderColor, HighlightWidth, true);
        }
        else if (OutlineStyle() is { } box)
        {
            DrawStyleBox(box, rect.Grow(HighlightWidth / 2f));
        }
    }

    /// "shape" in card_face: a column or one shape for every card. Anything but a disc or a square is a card.
    public bool IsPiece => Card != null && Shape() is Disc or SquarePiece;

    string Shape() => ColorField("shape");

    /// "size" in card_face: a column or one number of pixels across (also as text, as the editor writes it).
    float PieceSize()
    {
        object? setting = face.GetValueOrDefault("size");
        string size = setting is string text ? Card!.Column(text, text) : Values.Format(setting);
        return Values.IsValidFloat(size) ? (float)Values.ToFloat(size) : DefaultPieceSize;
    }

    /// A piece shows its color, or its "back" color while it's turned over (both sides are public); a piece this
    /// screen can't see shows the color of card backs. Its title is written on its face.
    void DrawPiece(Rect2 rect)
    {
        Color fill = !FaceUp ? HiddenPieceColor : Color.FromString(ColorField(Card!.FaceUp ? "background" : "back"), Colors.White);
        if (Shape() == Disc)
        {
            float radius = rect.Size.X / 2;
            DrawCircle(rect.GetCenter() + new Vector2(0, 2), radius, PieceShadow);
            DrawCircle(rect.GetCenter(), radius, fill);
            DrawArc(rect.GetCenter(), radius, 0, Mathf.Tau, 48, PieceBorder, 1.5f, true);
        }
        else
        {
            DrawStyleBox(PieceBox(fill), rect);
        }
        if (FaceUp && Card!.FaceUp)
        {
            Font font = GetThemeDefaultFont();
            string title = Field("title");
            int fontSize = FittingSize(font, title, rect.Size.X - 4, (int)(rect.Size.Y / 2.5f), MinTextFontSize);
            Color ink = Color.FromString(ColorField("color"), Colors.Black);
            DrawString(font, new Vector2(2, rect.GetCenter().Y + fontSize / 3f), title, HorizontalAlignment.Center, rect.Size.X - 4, fontSize, ink);
        }
    }

    static StyleBoxFlat PieceBox(Color fill)
    {
        if (!pieceStylesByColor.TryGetValue(fill, out StyleBoxFlat? box))
        {
            box = new() { BgColor = fill, BorderColor = PieceBorder, ShadowSize = 3, ShadowColor = PieceShadow };
            box.SetBorderWidthAll(1);
            box.SetCornerRadiusAll(4);
            pieceStylesByColor[fill] = box;
        }
        return box;
    }

    StyleBoxFlat? OutlineStyle()
    {
        if (Hint == DropHint.Allowed)
        {
            return AllowedStyle;
        }
        if (Hint == DropHint.Refused)
        {
            return RefusedStyle;
        }
        if (Selected)
        {
            return selectedStyle;
        }
        return Highlighted ? HighlightStyle : null;
    }

    void DrawFace(Rect2 rect)
    {
        DrawStyleBox(FaceBox(), rect);
        Font font = GetThemeDefaultFont();
        Color ink = Color.FromString(ColorField("color"), Colors.Black);
        float textWidth = CardSize.X - Margin * 2;
        DrawString(font, new Vector2(Margin, Margin + CornerFontSize), Field("corner"), HorizontalAlignment.Left, textWidth, CornerFontSize, ink);
        string rulesText = Field("text");
        string title = Field("title");
        float titleY = rulesText.Length > 0 ? CardSize.Y * 0.32f : CardSize.Y / 2;
        DrawString(font, new Vector2(Margin, titleY), title, HorizontalAlignment.Center, textWidth, FittingSize(font, title, textWidth), ink);
        float lineY = titleY + (rulesText.Length > 0 ? BodyFontSize + 6 : TitleFontSize);
        foreach (object? column in face.GetValueOrDefault("body") as List<object?> ?? [])
        {
            DrawString(font, new Vector2(Margin, lineY), Card!.Column(Values.Text(column)), HorizontalAlignment.Center, textWidth, BodyFontSize, ink);
            lineY += BodyFontSize + 4;
        }
        if (rulesText.Length > 0)
        {
            int textSize = FittingTextSize(font, rulesText, textWidth, CardSize.Y - Margin - lineY);
            DrawMultilineString(font, new Vector2(Margin, lineY + 2), rulesText, HorizontalAlignment.Center, textWidth, textSize, -1, ink);
        }
    }

    /// Marks show on both sides, so a player can see what they know about a card they can't see.
    void DrawMarks(Rect2 rect)
    {
        Font font = GetThemeDefaultFont();
        Rect2 band = new(rect.Position.X + 4, rect.End.Y - MarksHeight - 6, rect.Size.X - 8, MarksHeight);
        int fontSize = FittingSize(font, MarksText, band.Size.X - 4, MarksFontSize, MinTextFontSize);
        DrawRect(band, MarksBackground);
        DrawString(font, new Vector2(band.Position.X, band.End.Y - 5), MarksText, HorizontalAlignment.Center, band.Size.X, fontSize, Colors.White);
    }

    static int FittingTextSize(Font font, string text, float width, float height)
    {
        int fontSize = TextFontSize;
        while (fontSize > MinTextFontSize && font.GetMultilineStringSize(text, HorizontalAlignment.Center, width, fontSize).Y > height)
        {
            fontSize--;
        }
        return fontSize;
    }

    static int FittingSize(Font font, string text, float width, int largest = TitleFontSize, int smallest = MinTitleFontSize)
    {
        int fontSize = largest;
        while (fontSize > smallest && font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X > width)
        {
            fontSize--;
        }
        return fontSize;
    }

    StyleBoxFlat FaceBox()
    {
        Color background = Color.FromString(ColorField("background"), Colors.White);
        if (background == Colors.White)
        {
            return faceStyle;
        }
        if (!faceStylesByColor.TryGetValue(background, out StyleBoxFlat? box))
        {
            faceStylesByColor[background] = box = RoundedBox(background, FaceBorder);
        }
        return box;
    }

    /// "color", "background", "back" and "shape" name a column (each card its own) or give one for every card ("#f3e9d2").
    string ColorField(string facePart)
    {
        string setting = face.GetValueOrDefault(facePart) as string ?? "";
        return Card!.Column(setting, setting);
    }

    string Field(string facePart) => Card!.Column(face.GetValueOrDefault(facePart) as string ?? "");

    static StyleBoxFlat RoundedBox(Color fill, Color border)
    {
        StyleBoxFlat box = new() { BgColor = fill, BorderColor = border, ShadowSize = 3, ShadowColor = new(0, 0, 0, 0.3f) };
        box.SetBorderWidthAll(2);
        box.SetCornerRadiusAll(8);
        return box;
    }

    static StyleBoxFlat OutlineBox(Color color)
    {
        StyleBoxFlat box = new() { DrawCenter = false, BorderColor = color };
        box.SetBorderWidthAll(HighlightWidth);
        box.SetCornerRadiusAll(10);
        return box;
    }
}

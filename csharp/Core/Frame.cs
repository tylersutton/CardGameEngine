namespace Core;

/// What "from", "to", "clicked_zone" and "choice" mean for steps, kept with them so they keep their meaning if
/// the steps wait for a choice.
public record MoveContext(Zone? From, Zone? To, Zone? ClickedZone, object? Choice);

public enum FrameKind { List, Loop, While }

/// One list of steps being run by StepRunner, as plain data that undo and bot try-outs copy.
public class Frame
{
    public required List<object?> Steps { get; init; }
    /// The next step.
    public int Index { get; set; }
    public Card? Card { get; init; }
    /// How many triggers deep.
    public int Depth { get; init; }
    /// True for a whole trigger, action or rule; false for an "if" branch or loop body inside one.
    public bool Root { get; init; }
    public required MoveContext Context { get; init; }
    /// Names set with = that aren't game variables, and loop items.
    public Dictionary<string, object?> Locals { get; private set; } = [];
    /// Choices already made for the step at Index.
    public List<object?> Answers { get; set; } = [];
    public FrameKind Kind { get; init; } = FrameKind.List;
    /// The "turn" steps, and the "if" branches and loops inside them.
    public bool Turn { get; init; }

    // A loop: its name, items, which item it's on, and whose turn it was before (looping over player changes it).
    public string LoopName { get; init; } = "";
    public List<object?> Items { get; init; } = [];
    public int Position { get; set; }
    public object? OuterPlayer { get; set; }

    // A while: its condition, how often it has repeated, and whether it's been checked for this time round.
    public string Condition { get; init; } = "";
    public int Repeats { get; set; }
    public bool Checked { get; set; }

    /// A copy whose remembered names and answers can change without touching this one (for undo).
    public Frame Copy()
    {
        Frame copy = (Frame)MemberwiseClone();
        copy.Locals = new(Locals);
        copy.Answers = [.. Answers];
        return copy;
    }

    /// Everything about the frame as plain values, to tell whether two states are the same.
    public List<object?> Plain() =>
    [
        Steps, (long)Index, Card, (long)Depth, Root, Context.From, Context.To, Context.ClickedZone, Context.Choice,
        Locals, Answers, Kind.ToString(), Turn, LoopName, Items, (long)Position, OuterPlayer, Condition, (long)Repeats, Checked,
    ];
}

/// What a step that waits asks for: an offer of actions, or a choice.
public abstract record WaitRequest;

public record OfferRequest(List<string> Actions) : WaitRequest;

public record ChoiceRequest(List<object?> Options, int Amount, bool Exact, string Prompt) : WaitRequest;

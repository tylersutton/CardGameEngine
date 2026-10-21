namespace Core;

/// A step's text as a tree, from StepParser: "score += 2" becomes
/// new AssignNode("score", null, "+=", new ValueNode(2L)).
public abstract record StepNode;

/// A number (long or double), text, true, false or null.
public record ValueNode(object? Value) : StepNode;

public record ListNode(IReadOnlyList<StepNode> Items) : StepNode;

public record NameNode(string Name) : StepNode;

public record CallNode(string Name, IReadOnlyList<StepNode> Args) : StepNode;

/// card.suit
public record MemberNode(StepNode Target, string Name) : StepNode;

/// list[0], or hand[2] for player 2's own zone.
public record IndexNode(StepNode Target, StepNode Index) : StepNode;

/// - and not
public record UnaryNode(string Op, StepNode Term) : StepNode;

/// +, ==, and, in, ...
public record BinaryNode(string Op, StepNode Left, StepNode Right) : StepNode;

/// x if condition else y
public record ChooseNode(StepNode Condition, StepNode IfTrue, StepNode IfFalse) : StepNode;

/// =, += and -=, only as a whole step. Player is set for score[2] = 0, a player's own variable.
public record AssignNode(string Name, StepNode? Player, string Op, StepNode Value) : StepNode;

/// A step that didn't parse: working it out reports the error.
public record UnreadableNode(string Error) : StepNode;

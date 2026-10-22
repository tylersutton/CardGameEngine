using System.Collections;
using System.Reflection;
using System.Text;
using static Core.Values;

namespace Core;

/// Works out a step tree from StepParser against the game: names are looked up as they're met, building blocks
/// are called on the game's actions, and "and"/"or" stop as soon as the answer is known. Problems (a column the
/// card doesn't have, text compared with a number) set Failure instead of stopping the game. One per evaluation.
public class StepEvaluator(IStepContext game, Card? card)
{
    public const string CardVariable = "card";
    public const int MaxConditionDepth = 50;

    /// How many numbers each math function takes; min and max take two or more.
    public static readonly Dictionary<string, int> MathArguments = new()
    {
        ["abs"] = 1, ["floor"] = 1, ["ceil"] = 1, ["round"] = 1, ["int"] = 1, ["min"] = 2, ["max"] = 2,
    };

    public string Failure { get; private set; } = "";

    /// After a failure, or once a choose(...) is waiting for its answer: the rest of the step isn't worked out.
    public bool Stopped { get; private set; }

    object? actions;
    /// The parameters of the conditions being worked out (points(cards) called as points(hand)), innermost last.
    readonly Stack<Dictionary<string, object?>> parameters = new();

    public object? Evaluate(StepNode node)
    {
        if (Stopped)
        {
            return null;
        }
        return node switch
        {
            NameNode name => Name(name.Name),
            ValueNode value => value.Value,
            CallNode call => Call(call.Name, call.Args),
            BinaryNode binary => Binary(binary),
            ListNode list => list.Items.Select(Evaluate).ToList(),
            MemberNode member => Member(Evaluate(member.Target), member.Name),
            IndexNode { Target: NameNode name } index when game.IsPerPlayer(name.Name) => Numbered(name.Name, Evaluate(index.Index)),
            IndexNode index => Index(Evaluate(index.Target), Evaluate(index.Index)),
            UnaryNode unary => Unary(unary.Op, Evaluate(unary.Term)),
            ChooseNode choose => Evaluate(IsTrue(Evaluate(choose.Condition)) ? choose.IfTrue : choose.IfFalse),
            AssignNode assign => Assign(assign),
            UnreadableNode unreadable => Fail(unreadable.Error),
            _ => throw new ArgumentException($"unknown step node {node}"),
        };
    }

    object? Name(string name)
    {
        if (name == CardVariable)
        {
            return card;
        }
        if (parameters.TryPeek(out Dictionary<string, object?>? innermost) && innermost.TryGetValue(name, out object? parameter))
        {
            return parameter;
        }
        if (game.TryGetCondition(name, out Condition? condition))
        {
            if (condition.Parameters.Count > 0)
            {
                return Fail($"{name} needs its {string.Join(", ", condition.Parameters)}, e.g. {name}(...)");
            }
            return Evaluate(condition.Tree);
        }
        object? value = game.ValueOf(name);
        if (value == null && game.IsUnsetLocal(name))
        {
            return Fail($"\"{name}\" has no value here: it's remembered only in the list of steps that sets it (with = or a loop)");
        }
        return value;
    }

    object? Assign(AssignNode node)
    {
        string name = node.Player == null ? node.Name : NumberedName(node.Name, Evaluate(node.Player));
        object? value = Evaluate(node.Value);
        if (node.Op != "=" && !Stopped)
        {
            value = Operate(node.Op[..1], node.Player == null ? Name(name) : game.ValueOf(name), value);
        }
        if (!Stopped)
        {
            game.Assign(name, value);
        }
        return null;
    }

    /// hand[2], score[dealer]: that player's (or team's) own zone or variable.
    object? Numbered(string name, object? number)
    {
        string numbered = NumberedName(name, number);
        return Stopped ? null : game.ValueOf(numbered);
    }

    string NumberedName(string name, object? number)
    {
        if (Stopped)
        {
            return "";
        }
        if (IsNumber(number) && game.NumberedName(name, (int)ToDouble(number)) is { } numbered)
        {
            return numbered;
        }
        Fail($"{name}[{Format(number)}]: there's no {(game.IsPerTeam(name) ? "team" : "player")} {Format(number)}");
        return "";
    }

    object? Binary(BinaryNode node)
    {
        object? left = Evaluate(node.Left);
        return node.Op switch
        {
            "and" => IsTrue(left) && IsTrue(Evaluate(node.Right)),
            "or" => IsTrue(left) || IsTrue(Evaluate(node.Right)),
            _ => Operate(node.Op, left, Evaluate(node.Right)),
        };
    }

    object? Unary(string op, object? value)
    {
        if (Stopped)
        {
            return null;
        }
        return (op, value) switch
        {
            ("not", _) => !IsTrue(value),
            (_, long whole) => -whole,
            (_, double number) => -number,
            _ => Fail($"can't make {Describe(value)} negative"),
        };
    }

    /// The operators, with their meanings checked first: numbers with numbers (whole numbers stay whole, so 7 / 2
    /// is 3), text joined with + and compared, lists joined with +, and 'text %d' % [values] filled in.
    public object? Operate(string op, object? left, object? right)
    {
        if (Stopped)
        {
            return null;
        }
        if (op is "in" or "not in")
        {
            return Contains(op, left, right);
        }
        if (op is "==" or "!=")
        {
            if (!AreComparable(left, right))
            {
                return Fail($"can't compare {Describe(left)} with {Describe(right)}{QuoteHint(left, right)}");
            }
            return AreEqual(left, right) == (op == "==");
        }
        if (op == "%" && left is string template)
        {
            return FillIn(template, right);
        }
        bool bothNumbers = IsNumber(left) && IsNumber(right);
        switch (op, left, right)
        {
            case ("+", string leftText, string rightText):
                return leftText + rightText;
            case ("+", List<object?> leftItems, List<object?> rightItems):
                return leftItems.Concat(rightItems).ToList();
            case ("/" or "%", _, _) when bothNumbers && ToDouble(right) == 0:
                return Fail("can't divide by zero");
            case ("+" or "-" or "*" or "/" or "%", _, _) when bothNumbers:
                return Arithmetic(op, left!, right!);
            case ("<" or ">" or "<=" or ">=", _, _) when bothNumbers || left is string && right is string:
                return Compare(op, left!, right!);
        }
        string hint = StepParser.Comparisons.Contains(op) ? QuoteHint(left, right) : "";
        return Fail($"can't work out {Describe(left)} {op} {Describe(right)}{hint}");
    }

    static object Arithmetic(string op, object left, object right)
    {
        if (left is long a && right is long b)
        {
            return op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => a / b,
                _ => a % b,
            };
        }
        double x = ToDouble(left);
        double y = ToDouble(right);
        return op switch
        {
            "+" => x + y,
            "-" => x - y,
            "*" => x * y,
            "/" => x / y,
            _ => x % y,
        };
    }

    static bool Compare(string op, object left, object right)
    {
        int order = left is string leftText ? string.CompareOrdinal(leftText, (string)right) : ToDouble(left).CompareTo(ToDouble(right));
        return op switch
        {
            "<" => order < 0,
            ">" => order > 0,
            "<=" => order <= 0,
            _ => order >= 0,
        };
    }

    /// "x in list": an item of a list, a card in a zone, or text inside text.
    object? Contains(string op, object? item, object? container)
    {
        bool found;
        switch (container)
        {
            case List<object?> items:
                found = items.Any(each => AreComparable(each, item) && AreEqual(each, item));
                break;
            case Zone zone:
                found = item is Card card && zone.Cards.Contains(card);
                break;
            case string text when item is string part:
                found = text.Contains(part);
                break;
            default:
                return Fail($"can't look for {Describe(item)} in {Describe(container)}");
        }
        return found == (op == "in");
    }

    static string QuoteHint(object? left, object? right) =>
        left is string && IsNumber(right) || right is string && IsNumber(left)
            ? " (to compare with text, put the number in quotes: '10')"
            : "";

    /// 'text' % values fills in %s and %d (and %% for a percent sign), one value for each.
    object? FillIn(string template, object? values)
    {
        Queue<object?> queue = new(values as List<object?> ?? [values]);
        StringBuilder result = new();
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] != '%' || i + 1 >= template.Length)
            {
                result.Append(template[i]);
                continue;
            }
            i++;
            switch (template[i])
            {
                case '%':
                    result.Append('%');
                    break;
                case 's' when queue.Count > 0:
                    result.Append(Text(queue.Dequeue()));
                    break;
                case 'd' when queue.Count > 0 && IsNumber(queue.Peek()):
                    result.Append((long)ToDouble(queue.Dequeue()));
                    break;
                default:
                    return Fail($"can't fill in '{template}' with {Describe(values)}");
            }
        }
        if (queue.Count > 0)
        {
            return Fail($"can't fill in '{template}' with {Describe(values)}");
        }
        return result.ToString();
    }

    object? Member(object? target, string name)
    {
        if (Stopped)
        {
            return null;
        }
        switch (target)
        {
            case null:
                return Fail($"can't read .{name} of nothing (if the card might be missing, text() and number() give '' and 0)");
            case IDictionary dictionary:
                return dictionary.Contains(name) ? dictionary[name] : Fail($"no column \"{name}\" in this card's deck");
            case Card card:
                return card.TryRead(name, out object? column) ? column : Fail($"no column \"{name}\" in this card's deck");
            case Zone zone when zone.TryRead(name, out object? value):
                return value;
        }
        return Fail($"{Describe(target)} has no \"{name}\"");
    }

    object? Index(object? target, object? index)
    {
        if (Stopped)
        {
            return null;
        }
        if (target is List<object?> items && IsNumber(index))
        {
            int position = (int)ToDouble(index);
            if (position >= -items.Count && position < items.Count)
            {
                return items[position < 0 ? items.Count + position : position];
            }
            return Fail($"position {position} is outside the list (it has {items.Count} items, from position 0)");
        }
        if (target is IDictionary dictionary && index != null && dictionary.Contains(index))
        {
            return dictionary[index];
        }
        return Fail($"can't read position {Format(index)} of {Describe(target)}");
    }

    object? Call(string name, IReadOnlyList<StepNode> argNodes)
    {
        List<object?> args = argNodes.Select(Evaluate).ToList();
        if (Stopped)
        {
            return null;
        }
        if (MathArguments.ContainsKey(name))
        {
            return MathFunction(name, args);
        }
        if (game.TryGetCondition(name, out Condition? condition))
        {
            return ConditionCall(name, condition, args);
        }
        if (!game.Blocks.Has(name))
        {
            return Fail($"unknown building block \"{name}\"");
        }
        if (game.Blocks.ArgumentCountProblem(name, args.Count) is { } countProblem)
        {
            return Fail(countProblem);
        }
        ParameterInfo[] parameters = game.Blocks.Parameters(name);
        for (int i = 0; i < args.Count; i++)
        {
            ParameterInfo parameter = parameters[i];
            if (!Fits(args[i], parameter.ParameterType))
            {
                return Fail($"{name}: \"{BuildingBlocks.ParameterName(parameter)}\" should be {KindOfParameter(parameter.ParameterType)}, not {Describe(args[i])} ({game.Blocks.Signature(name)})");
            }
            args[i] = Convert(args[i], parameter.ParameterType);
        }
        actions ??= game.CreateActions(card);
        object? result = game.Blocks.Call(actions, name, args);
        Stopped = game.Waiting;
        return result;
    }

    /// Whether the value is the kind of thing the building block's parameter takes. A card parameter may be null
    /// (no card), since building blocks like number(card, ...) allow for it; a zone parameter may not.
    static bool Fits(object? value, Type type)
    {
        if (type == typeof(long) || type == typeof(int) || type == typeof(double))
        {
            return IsNumber(value);
        }
        if (type == typeof(string))
        {
            return value is string;
        }
        if (type == typeof(List<object?>))
        {
            return value is List<object?>;
        }
        if (type == typeof(Zone))
        {
            return value is Zone;
        }
        if (type == typeof(Card))
        {
            return value is null or Card;
        }
        return true;
    }

    static object? Convert(object? value, Type type)
    {
        if (type == typeof(long))
        {
            return (long)ToDouble(value);
        }
        if (type == typeof(int))
        {
            return (int)ToDouble(value);
        }
        if (type == typeof(double))
        {
            return ToDouble(value);
        }
        if (type == typeof(bool))
        {
            return IsTrue(value);
        }
        return value;
    }

    static string KindOfParameter(Type type)
    {
        if (type == typeof(long) || type == typeof(int) || type == typeof(double))
        {
            return "a number";
        }
        if (type == typeof(string))
        {
            return "text in quotes";
        }
        if (type == typeof(List<object?>))
        {
            return "a list";
        }
        if (type == typeof(Zone))
        {
            return "a zone";
        }
        return type == typeof(Card) ? "a card" : "something else";
    }

    /// A condition with parameters, like points(cards), worked out with them set to the arguments.
    object? ConditionCall(string name, Condition condition, List<object?> args)
    {
        IReadOnlyList<string> names = condition.Parameters;
        if (args.Count != names.Count)
        {
            return Fail($"{name} takes {names.Count} ({string.Join(", ", names)}), got {args.Count}");
        }
        if (parameters.Count >= MaxConditionDepth)
        {
            return Fail($"{name} called itself {MaxConditionDepth} deep");
        }
        parameters.Push(names.Zip(args).ToDictionary(pair => pair.First, pair => pair.Second));
        object? result = Evaluate(condition.Tree);
        parameters.Pop();
        return result;
    }

    object? MathFunction(string name, List<object?> args)
    {
        if (name == "int" && args is [bool flag])
        {
            return flag ? 1L : 0L;
        }
        if (name is "min" or "max" && args is [List<object?> items])
        {
            if (items.Count == 0)
            {
                return Fail($"{name} of an empty list");
            }
            args = items;
        }
        if (!args.All(IsNumber))
        {
            return Fail($"{name} needs numbers, not {string.Join(", ", args.Select(Describe))}");
        }
        // Like GDScript's, these keep whole numbers whole.
        return (name, args[0]) switch
        {
            ("abs", long whole) => Math.Abs(whole),
            ("abs", double number) => Math.Abs(number),
            ("floor", double number) => Math.Floor(number),
            ("ceil", double number) => Math.Ceiling(number),
            ("round", double number) => Math.Round(number, MidpointRounding.AwayFromZero),
            ("floor" or "ceil" or "round", long whole) => whole,
            ("int", _) => (long)ToDouble(args[0]),
            ("min", _) => args.MinBy(ToDouble),
            _ => args.MaxBy(ToDouble),
        };
    }

    object? Fail(string message)
    {
        Stopped = true;
        if (Failure.Length == 0)
        {
            Failure = message;
        }
        return null;
    }
}

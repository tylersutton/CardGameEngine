using Core;

namespace Editing;

/// The game's turn in words, for the editor: each step of "turn" in order, with its offers spelled out (which actions,
/// done how), and then the actions that can be done any time and those only asked for by steps.
public static class TurnSummary
{
    const string Indent = "    ";

    public static List<string> Lines(GameRules rules)
    {
        List<string> summary = [];
        if (rules.Turn.Count == 0)
        {
            summary.Add("No turn: players do whatever their actions allow, whenever they allow it.");
        }
        else
        {
            summary.Add("Each turn, for whoever's turn it is:");
            DescribeSteps(rules, rules.Turn, 1, summary);
        }
        List<string> offered = OfferedNames(rules);
        List<string> asked = rules.Actions.Where(pair => !offered.Contains(pair.Key) && pair.Value.GetValueOrDefault("button") is false && !Plays.IsCardAction(pair.Value))
            .Select(pair => pair.Key).ToList();
        List<string> anyTime = rules.Actions.Keys.Where(name => !offered.Contains(name) && !asked.Contains(name)).ToList();
        if (anyTime.Count > 0)
        {
            summary.Add("Any time: " + string.Join(", ", anyTime.Select(name => How(rules, name))));
        }
        if (asked.Count > 0)
        {
            summary.Add("Only when a step asks for it (ask): " + string.Join(", ", asked));
        }
        return summary;
    }

    /// How an action is done: "Play a card (drag hand → pile)", "Draw (click deck)", "Pass (button)".
    public static string How(GameRules rules, string actionName)
    {
        Dictionary<string, object?> action = rules.Actions.GetValueOrDefault(actionName) ?? [];
        List<string> ways = [];
        if (action.GetValueOrDefault("drag") is Dictionary<string, object?> drag)
        {
            ways.Add($"drag {ZonesText(drag.GetValueOrDefault("from"))} → {ZonesText(drag.GetValueOrDefault("to"))}");
        }
        foreach (string clickKey in new[] { "click", "double_click" })
        {
            object? binding = action.GetValueOrDefault(clickKey);
            string word = clickKey.Replace("_", "-");
            if (binding is true)
            {
                ways.Add($"or {word} it");
            }
            else if (binding != null && binding is not bool)
            {
                ways.Add($"{word} {ZonesText(binding)}");
            }
        }
        if (action.GetValueOrDefault("button") as bool? ?? !Plays.IsCardAction(action))
        {
            ways.Add("button");
        }
        return $"{actionName} ({(ways.Count > 0 ? string.Join(" ", ways) : "button")})";
    }

    static void DescribeSteps(GameRules rules, List<object?> steps, int depth, List<string> summary)
    {
        string indent = string.Concat(Enumerable.Repeat(Indent, depth));
        foreach (object? step in steps)
        {
            if (step is string text)
            {
                summary.Add(indent + DescribeStep(rules, text));
            }
            else if (step is Dictionary<string, object?> block && block.TryGetValue("if", out object? condition))
            {
                summary.Add(indent + $"If {condition}:");
                DescribeSteps(rules, StepsOf(block, "then"), depth + 1, summary);
                if (StepsOf(block, "else").Count > 0)
                {
                    summary.Add(indent + "Otherwise:");
                    DescribeSteps(rules, StepsOf(block, "else"), depth + 1, summary);
                }
            }
            else if (step is Dictionary<string, object?> loop && loop.ContainsKey("for"))
            {
                summary.Add(indent + $"For each {loop["for"]} in {loop.GetValueOrDefault("in")}:");
                DescribeSteps(rules, StepsOf(loop, "do"), depth + 1, summary);
            }
            else if (step is Dictionary<string, object?> repeat && repeat.TryGetValue("while", out object? whileCondition))
            {
                summary.Add(indent + $"Again and again while {whileCondition}:");
                DescribeSteps(rules, StepsOf(repeat, "do"), depth + 1, summary);
            }
        }
    }

    /// A step as written, unless it offers actions: then which ones, and how each is done.
    static string DescribeStep(GameRules rules, string step)
    {
        StepNode tree = StepParser.Parse(step);
        foreach (CallNode call in Calls(tree))
        {
            List<string> hows = (call.Args.Count > 0 ? NamesGiven(call.Args[0]) : []).Select(name => How(rules, name)).ToList();
            if (call.Name == "offer")
            {
                return "Offer " + string.Join(" or ", hows);
            }
            if (call.Name == "offer_until" && call.Args.Count == 2)
            {
                IEnumerable<string> enders = NamesGiven(call.Args[1]).Select(name => How(rules, name));
                return $"Offer {string.Join(" or ", hows)} again and again, until {string.Join(" or ", enders)}";
            }
        }
        return step;
    }

    static List<CallNode> Calls(StepNode node)
    {
        List<CallNode> found = node is CallNode call && StepCompiler.OfferBlocks.Contains(call.Name) ? [call] : [];
        foreach (StepNode child in StepCompiler.Children(node))
        {
            found.AddRange(Calls(child));
        }
        return found;
    }

    static List<string> NamesGiven(StepNode arg)
    {
        IEnumerable<StepNode> items = arg is ListNode list ? list.Items : [arg];
        return items.OfType<ValueNode>().Select(item => item.Value).OfType<string>().ToList();
    }

    /// Every action named in an offer anywhere: they can only be done while offered.
    static List<string> OfferedNames(GameRules rules)
    {
        List<string> texts = [];
        CollectTexts(rules.Turn, texts);
        foreach (object? steps in rules.Triggers.Values)
        {
            CollectTexts(steps, texts);
        }
        foreach (Dictionary<string, object?> action in rules.Actions.Values)
        {
            CollectTexts(action.GetValueOrDefault("steps") ?? new List<object?>(), texts);
        }
        List<string> names = [];
        foreach (string text in texts)
        {
            foreach (CallNode call in Calls(StepParser.Parse(text)))
            {
                foreach (StepNode arg in call.Args.Take(2))
                {
                    names.AddRange(NamesGiven(arg));
                }
            }
        }
        return names;
    }

    static void CollectTexts(object? steps, List<string> texts)
    {
        foreach (object? step in steps as List<object?> ?? [steps])
        {
            if (step is string text)
            {
                texts.Add(text);
            }
            else if (step is Dictionary<string, object?> block)
            {
                texts.AddRange(new[] { "if", "in", "while" }.Select(key => block.GetValueOrDefault(key)).OfType<string>());
                foreach (string key in new[] { "then", "else", "do" })
                {
                    CollectTexts(block.GetValueOrDefault(key) ?? new List<object?>(), texts);
                }
            }
        }
    }

    static List<object?> StepsOf(Dictionary<string, object?> block, string key) => block.GetValueOrDefault(key) as List<object?> ?? [];

    static string ZonesText(object? zones) => zones switch
    {
        null => "anywhere",
        List<object?> list => string.Join(", ", list.Select(Values.Text)),
        _ => Values.Text(zones),
    };
}

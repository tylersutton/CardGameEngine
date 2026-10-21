using System.Diagnostics;
using Core;

// The same workload as the GDScript benchmark: parse and evaluate representative steps.
string[] parsed =
[
    "20 * (sum(my_cards, 'coins') - count(matching(my_cards, 'is_copper'))) / count(my_cards) + 4 * min(count(matching(my_cards, 'is_terminal')), floor(count(my_cards) / 10)) + 0.01 * coins",
    "(count(to) == 0 and number(card, 'value') == 13) or (number(card, 'value') == number(top(to), 'value') - 1 and card.data.color != text(top(to), 'color'))",
    "score[dealer] += 2 if card.rank == 'J' else 0",
    "'%s has %d' % ['deck', count(deck)]",
];
string[] evaluated =
[
    "1 + 2 * 3 - 4 / 2 + (7 % 3) * 10",
    "phase == 'draw' and total == 0 or score > 3",
    "card.value + 1 if card.rank != 'K' else 0",
    "is_ace and not deck_full or score == opponent_score",
    "lookup(table, 2) + max(1, 2, 3)",
    "hearts_and_spades(deck)",
];
const int ParseRounds = 20000;
const int EvaluateRounds = 20000;
const int SlowRounds = 200;

TestGame game = new();
Card ace = game.Cards.First(card => card.Title == "A♠");
StepNode[] trees = evaluated.Select(game.TreeOf).ToArray();
// Warm up the JIT, as a game would be by its second turn.
for (int i = 0; i < 200; i++)
{
    foreach (string text in parsed)
    {
        StepParser.Parse(text);
    }

    foreach (StepNode tree in trees)
    {
        new StepEvaluator(game, ace).Evaluate(tree);
    }
}

Stopwatch clock = Stopwatch.StartNew();
for (int i = 0; i < ParseRounds; i++)
{
    foreach (string text in parsed)
    {
        StepParser.Parse(text);
    }
}

Report("parse", ParseRounds * parsed.Length, clock);

clock.Restart();
for (int i = 0; i < EvaluateRounds; i++)
{
    foreach (StepNode tree in trees[..5])
    {
        new StepEvaluator(game, ace).Evaluate(tree);
    }
}

Report("evaluate (5 small steps)", EvaluateRounds * 5, clock);

clock.Restart();
for (int i = 0; i < SlowRounds; i++)
{
    new StepEvaluator(game, ace).Evaluate(trees[5]);
}

Report("evaluate hearts_and_spades(deck)", SlowRounds, clock);

static void Report(string what, int count, Stopwatch clock) =>
    Console.WriteLine($"{what,-34} {count,8} in {clock.ElapsedMilliseconds,6} ms = {clock.Elapsed.TotalMicroseconds / count,7:F2} us each");

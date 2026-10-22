using System.Text.Json;
using Core;

/// RandomNumbers against numbers Godot gave for the same seeds (Reference/random_numbers.json, from
/// tests/record_reference.gd): raw numbers, ranges, a deck shuffle and the state left at the end.
public class RandomNumbersTests
{
    public record Sequence(string Seed, List<uint> Numbers, List<List<int>> Ranges, List<int> Shuffled, string State);

    public static TheoryData<string> Seeds()
    {
        TheoryData<string> seeds = [];
        foreach (Sequence sequence in Sequences())
        {
            seeds.Add(sequence.Seed);
        }
        return seeds;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void MatchesGodot(string seed)
    {
        Sequence wanted = Sequences().Single(sequence => sequence.Seed == seed);
        // Godot keeps seeds and states as signed 64-bit numbers; the bits are what matter.
        RandomNumbers random = new(unchecked((ulong)long.Parse(seed)));
        Assert.Equal(wanted.Numbers, wanted.Numbers.Select(_ => random.NextUInt()));
        foreach (List<int> range in wanted.Ranges)
        {
            Assert.Equal(range[2], random.NextInRange(range[0], range[1]));
        }
        Zone deck = new("deck", []);
        deck.Cards.AddRange(Enumerable.Range(0, 52).Select(id => new Card([]) { Id = id }));
        deck.Shuffle(random);
        Assert.Equal(wanted.Shuffled, deck.Cards.Select(card => card.Id));
        Assert.Equal(long.Parse(wanted.State), unchecked((long)random.State));
    }

    [Fact]
    public void RestoringTheStateRepeatsTheNumbers()
    {
        RandomNumbers random = new(42);
        ulong saved = random.State;
        List<int> first = Enumerable.Range(0, 10).Select(_ => random.NextInRange(1, 6)).ToList();
        random.State = saved;
        Assert.Equal(first, Enumerable.Range(0, 10).Select(_ => random.NextInRange(1, 6)));
    }

    static List<Sequence> Sequences() =>
        JsonSerializer.Deserialize<List<Sequence>>(File.ReadAllText(RepositoryFiles.PathOf("csharp/Core.Tests/Reference/random_numbers.json")), ReferenceMatch.JsonOptions)!;
}

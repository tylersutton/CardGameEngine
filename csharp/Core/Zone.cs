namespace Core;

/// Only what steps read from a zone; the full Zone comes with the rest of the core.
public class Zone(string name)
{
    public string Name { get; } = name;
    public List<Card> Cards { get; } = [];

    /// The same swaps as Zone.shuffle in GDScript, so the same numbers give the same order.
    public void Shuffle(RandomNumbers random)
    {
        for (int i = Cards.Count - 1; i > 0; i--)
        {
            int j = random.NextInRange(0, i);
            (Cards[i], Cards[j]) = (Cards[j], Cards[i]);
        }
    }

    public bool TryRead(string member, out object? value)
    {
        value = member switch
        {
            "name" => Name,
            "cards" => Cards.Cast<object?>().ToList(),
            _ => null,
        };
        return value != null;
    }
}

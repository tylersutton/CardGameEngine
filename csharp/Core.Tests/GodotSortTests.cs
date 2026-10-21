using System.Text.Json;
using Core;

/// GodotSort against the orders Godot's own sort gave (Reference/sorting.json, from tests/record_reference.gd).
public class GodotSortTests
{
    public record SortCase(List<int> Keys, List<int> Order);

    [Fact]
    public void TiesEndUpWhereGodotPutsThem()
    {
        List<SortCase> cases = JsonSerializer.Deserialize<List<SortCase>>(File.ReadAllText(RepositoryFiles.PathOf("csharp/Core.Tests/Reference/sorting.json")), ReferenceMatch.JsonOptions)!;
        Assert.NotEmpty(cases);
        foreach (SortCase sortCase in cases)
        {
            List<int> positions = Enumerable.Range(0, sortCase.Keys.Count).ToList();
            GodotSort.Sort(positions, (a, b) => sortCase.Keys[a] < sortCase.Keys[b]);
            Assert.Equal(sortCase.Order, positions);
        }
    }

    [Fact]
    public void SortsBadlySplitListsToo()
    {
        // Sawtooth lists push quicksort towards its depth limit, where heapsort takes over.
        List<int> numbers = Enumerable.Range(0, 2000).Select(i => i % 37 * 1000 - i).ToList();
        GodotSort.Sort(numbers, (a, b) => a < b);
        Assert.Equal(numbers.Order(), numbers);
    }
}

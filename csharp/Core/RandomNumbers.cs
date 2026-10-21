using System.Numerics;

namespace Core;

/// Godot's RandomNumberGenerator (PCG32), number for number, so a seed deals the same cards in both engines and
/// recorded matches replay. All randomness that changes the game goes through the game's one RandomNumbers.
public class RandomNumbers
{
    const ulong Multiplier = 6364136223846793005;
    // Godot's default PCG stream, as pcg32_srandom_r turns it into an increment.
    const ulong Increment = unchecked((1442695040888963407UL << 1) | 1);

    /// Everything about where the sequence is; saving and restoring it (undo, bot try-outs) repeats the numbers.
    public ulong State { get; set; }

    public RandomNumbers(ulong seed)
    {
        Seed(seed);
    }

    /// rng.seed = seed in GDScript.
    public void Seed(ulong seed)
    {
        State = 0;
        NextUInt();
        State += seed;
        NextUInt();
    }

    /// randi() in GDScript.
    public uint NextUInt()
    {
        ulong old = State;
        State = old * Multiplier + Increment;
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rotation = (int)(old >> 59);
        return BitOperations.RotateRight(xorShifted, rotation);
    }

    /// randi_range(from, to) in GDScript: a whole number from one to the other, both included.
    public int NextInRange(int from, int to)
    {
        if (from == to)
        {
            return from;
        }
        uint bound = (uint)Math.Abs(from - to) + 1;
        // Numbers below the threshold would make some results likelier than others, so they're drawn again.
        uint threshold = (0u - bound) % bound;
        while (true)
        {
            uint number = NextUInt();
            if (number >= threshold)
            {
                return (int)(number % bound) + Math.Min(from, to);
            }
        }
    }
}

using Sim.Core;

namespace Sim.Tests;

public class RngStreamTests
{
    // Same seed, cycle, and name must always give the same draws.
    [Fact]
    public void Same_inputs_give_the_same_sequence()
    {
        RngStream a = RngStream.For(42, 3, "missions");
        RngStream b = RngStream.For(42, 3, "missions");

        Assert.Equal(Draw(a, 20), Draw(b, 20));
    }

    // A new mechanic drawing from its own stream must not disturb existing streams.
    [Theory]
    [InlineData(42UL, 3, "storylets")]
    [InlineData(42UL, 4, "missions")]
    [InlineData(43UL, 3, "missions")]
    public void Different_inputs_give_different_sequences(ulong seed, int cycle, string name)
    {
        RngStream baseline = RngStream.For(42, 3, "missions");
        RngStream other = RngStream.For(seed, cycle, name);

        Assert.NotEqual(Draw(baseline, 20), Draw(other, 20));
    }

    // A stream restored from its saved state continues where it stopped.
    [Fact]
    public void Restoring_state_continues_the_sequence()
    {
        RngStream original = RngStream.For(7, 1, "missions");
        Draw(original, 5);
        var restored = new RngStream(original.State);

        Assert.Equal(Draw(original, 10), Draw(restored, 10));
    }

    // Guards the algorithm itself: changing it would silently change every legacy.
    [Fact]
    public void Algorithm_is_pinned()
    {
        RngStream stream = RngStream.For(1, 1, "golden");

        Assert.Equal(new ulong[] { 5517430893459967052UL, 16803158252055916467UL, 1249752661054523924UL }, Draw(stream, 3));
    }

    // The mapping from raw draws to dice is part of every golden seed.
    [Fact]
    public void Dice_mapping_is_pinned()
    {
        RngStream dice = RngStream.For(1, 1, "golden");
        RngStream picks = RngStream.For(1, 1, "golden");

        Assert.Equal(new[] { 2, 6, 1, 1, 1, 4, 6, 6 }, Enumerable.Range(0, 8).Select(_ => dice.D6()));
        Assert.Equal(new[] { 29, 91, 6, 16, 3 }, Enumerable.Range(0, 5).Select(_ => picks.Next(100)));
    }

    [Fact]
    public void Dice_stay_in_range()
    {
        RngStream stream = RngStream.For(1, 1, "dice");
        for (int i = 0; i < 1000; i++)
        {
            Assert.InRange(stream.D6(), 1, 6);
            Assert.InRange(stream.TwoD6(), 2, 12);
            Assert.InRange(stream.Next(10), 0, 9);
        }
    }

    private static ulong[] Draw(RngStream stream, int count)
    {
        return Enumerable.Range(0, count).Select(_ => stream.NextUInt64()).ToArray();
    }
}

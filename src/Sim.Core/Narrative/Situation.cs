using Sim.Core.Content;

namespace Sim.Core.Narrative;

public enum FactType
{
    Text,
    Number,
    Flag,
    Set,
}

// One fact a line can test. Exactly one of the value members is meaningful, as Type says.
public sealed record FactValue(FactType Type, string? Text = null, int Number = 0, bool Flag = false, IReadOnlyList<string>? Set = null)
{
    public static FactValue Of(string text) => new(FactType.Text, Text: text);

    public static FactValue Of(int number) => new(FactType.Number, Number: number);

    public static FactValue Of(bool flag) => new(FactType.Flag, Flag: flag);

    public static FactValue Of(IReadOnlyList<string> set) => new(FactType.Set, Set: set);
}

// Someone a line can name and refer to.
public sealed record Person(string Id, string Name, PronounSet Pronouns);

// What a line is chosen for and expanded against: the beat kind, its facts (people's stats included, as "actor.caution"), the people
// by role, and the display names of the things it mentions by role ("encounter"). Nothing about missions is needed to read it.
public sealed record Situation(
    string Beat,
    IReadOnlyDictionary<string, FactValue> Facts,
    IReadOnlyDictionary<string, Person> People,
    IReadOnlyDictionary<string, string> Words);

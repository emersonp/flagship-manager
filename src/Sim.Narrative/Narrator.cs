using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Sim.Narrative;

// The lines engine behind INarrator (slice 1e part five): the pack's best line for a situation, expanded to plain text. Nothing is
// remembered between calls, so no line is passed over for having been used. The loader refuses a script in a chronicle line and in an
// option's chronicle text, so the text this expands is narration only; its entries, if it has several, are joined by a space.
public sealed class Narrator(ContentPack pack) : INarrator
{
    private readonly IReadOnlyDictionary<string, SymbolDef> symbols = pack.Symbols.ToDictionary(s => s.Id);

    public string? Line(Situation situation, string key)
    {
        return LineSelector.Select(pack.Lines, situation, new HashSet<string>(), key) is { } line ? Expand(line.Text, situation, key) : null;
    }

    public string? Expand(string text, Situation situation, string key)
    {
        try
        {
            Script script = Scripts.Parse(text);
            if (script.Entries.Count == 0 || script.Summary is not null || script.Entries.Any(e => e.Kind != EntryKind.Narration))
            {
                return null;
            }

            return string.Join(" ", script.Entries.Select(e => string.Concat(Expander.Expand(e.Text, situation, symbols, key).Select(p => p.Text))));
        }
        catch (Exception e) when (e is ExpansionException or NarrativeFormatException)
        {
            return null;
        }
    }
}

using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Narrative;

namespace Web.Forms;

// What the sample missions show about the lines (slice 1d design, section 2): shared lines never chosen, and beat kinds that met
// no line (the log then shows the systems line, and the report leaves the beat out). Advice, not refusal: a rare situation may
// simply not occur. An encounter's or an objective's own text is not listed as never chosen: it waits for its own moment.
public sealed record LineLint(IReadOnlyList<string> NeverChosen, IReadOnlyList<string> Without, IReadOnlyList<string> ReportWithout, int Samples)
{
    public bool Clean => NeverChosen.Count == 0 && Without.Count == 0;

    public static LineLint Of(ContentPack pack)
    {
        IReadOnlyList<SampleBeat> samples = NarrativeSamples.Of(pack);
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        var without = new SortedSet<string>(StringComparer.Ordinal);
        var reportWithout = new SortedSet<string>(StringComparer.Ordinal);
        var none = new HashSet<string>();
        foreach (SampleBeat sample in samples)
        {
            if (LineSelector.Select(pack.Lines, sample.Situation, none, sample.Key) is { } line)
            {
                chosen.Add(line.Id);
                continue;
            }

            without.Add(sample.Situation.Beat);
            if (Web.Bridge.Report.Reports(sample.Situation))
            {
                reportWithout.Add(sample.Situation.Beat);
            }
        }

        List<string> neverChosen = pack.Lines.Where(l => l.Owner is null).Select(l => l.Id).Where(id => !chosen.Contains(id)).Order(StringComparer.Ordinal).ToList();
        return new LineLint(neverChosen, without.ToList(), reportWithout.ToList(), samples.Count);
    }
}

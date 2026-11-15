using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Narrative;

namespace Web.Forms;

// What the sample missions show about the lines (slice 1d design, section 2): lines never chosen, and beat kinds that met no line
// (the log then shows the systems line; the report leaves the beat out). Advice, not refusal: a rare situation may simply not occur.
public sealed record LineLint(IReadOnlyList<string> NeverChosen, IReadOnlyList<string> LogWithout, IReadOnlyList<string> ReportWithout, int Samples)
{
    public bool Clean => NeverChosen.Count == 0 && LogWithout.Count == 0 && ReportWithout.Count == 0;

    public static LineLint Of(ContentPack pack)
    {
        IReadOnlyList<SampleBeat> samples = NarrativeSamples.Of(pack);
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        var logWithout = new SortedSet<string>(StringComparer.Ordinal);
        var reportWithout = new SortedSet<string>(StringComparer.Ordinal);
        var none = new HashSet<string>();
        foreach (SampleBeat sample in samples)
        {
            foreach (string use in NarrativeFacts.Uses)
            {
                if (use == "report" && !Web.Bridge.Report.Reports(sample.Situation))
                {
                    continue;
                }

                if (LineSelector.Select(pack.Lines, sample.Situation, use, none, sample.Key) is { } line)
                {
                    chosen.Add(line.Id);
                }
                else
                {
                    (use == "log" ? logWithout : reportWithout).Add(sample.Situation.Beat);
                }
            }
        }

        List<string> neverChosen = pack.Lines.Select(l => l.Id).Where(id => !chosen.Contains(id)).Order(StringComparer.Ordinal).ToList();
        return new LineLint(neverChosen, logWithout.ToList(), reportWithout.ToList(), samples.Count);
    }
}

using Sim.Core;
using Sim.Missions;

namespace Web.Bridge;

// The aftermath's report: sentences chosen from the beats that mattered, in order, and the decisions with their answers.
public sealed record ReportView(IReadOnlyList<IReadOnlyList<LinePart>> Sentences, IReadOnlyList<IReadOnlyList<LinePart>> Decisions);

public static class Report
{
    public const int Fewest = 3;
    public const int Most = 8;

    // Which beats make the report (slice 1d design, section 3). Rank 1, the result included, always goes in; rank 2 fills to Most; rank 3 (stage openers)
    // fills to Fewest. Each sentence is the report's sentence of the beat's line: a one-sentence line is its own, a script gives
    // one after "=", and a script that gives none leaves the beat to the shared line. A beat with no sentence is left out rather
    // than shown as systems text.
    public static ReportView Build(IReadOnlyList<MissionBeat> beats, IReadOnlyList<BeatProse> log, Names names)
    {
        var ranked = new List<(int Position, int Rank)>();
        for (int i = 0; i < beats.Count; i++)
        {
            if (Rank(beats[i].Beat) is { } rank)
            {
                ranked.Add((i, rank));
            }
        }

        var chosen = new List<int>();
        void Fill(int rank, int upTo)
        {
            foreach ((int position, _) in ranked.Where(r => r.Rank == rank))
            {
                if (chosen.Count >= upTo)
                {
                    return;
                }

                chosen.Add(position);
            }
        }

        Fill(1, int.MaxValue);
        Fill(2, Most);
        Fill(3, Fewest);
        // Too many must-haves: keep the earliest, but always the result.
        if (chosen.Count > Most)
        {
            int? result = chosen.Cast<int?>().FirstOrDefault(p => beats[p!.Value].Beat is ResultBeat);
            List<int> kept = chosen.Where(p => p != result).Order().Take(result is null ? Most : Most - 1).ToList();
            if (result is { } last)
            {
                kept.Add(last);
            }

            chosen = kept;
        }

        List<IReadOnlyList<LinePart>> sentences = chosen.Order().Where(p => log[p].Report is not null).Select(p => log[p].Report!).ToList();

        return new ReportView(sentences, Decisions(beats, names));
    }

    private static int? Rank(Beat beat) => beat switch
    {
        InjuryBeat b => b.Outcome == InjuryOutcome.Unharmed ? null : 1,
        CallBeat or InitiativeBeat or AnswerBeat or CollapseBeat or ResultBeat => 1,
        CheckBeat { Tier: Tier.Critical or Tier.Disaster } => 1,
        CheckBeat { Tier: Tier.Failure } or LeadChangeBeat => 2,
        StageBeat => 3,
        _ => null,
    };

    // Whether the report can use this beat, read from its situation: the same beats Rank ranks, for the lint.
    public static bool Reports(Sim.Core.Narrative.Situation situation)
    {
        string? Text(string fact) => situation.Facts.TryGetValue(fact, out Sim.Core.Narrative.FactValue? value) ? value.Text : null;
        return situation.Beat switch
        {
            "injury" => Text("outcome") != "unharmed",
            "check" => Text("tier") is "critical" or "disaster" or "failure",
            "call" or "initiative" or "answer" or "collapse" or "result" or "lead" or "stage" => true,
            _ => false,
        };
    }

    public static bool Reports(Beat beat) => Rank(beat) is not null;

    // Each call with the captain's answer, and each initiative with what the lead chose, in systems words.
    private static List<IReadOnlyList<LinePart>> Decisions(IReadOnlyList<MissionBeat> beats, Names names)
    {
        var decisions = new List<IReadOnlyList<LinePart>>();
        CallBeat? open = null;
        foreach (Beat beat in beats.Select(b => b.Beat))
        {
            switch (beat)
            {
                case CallBeat call:
                    open = call;
                    break;
                case AnswerBeat answer when open is not null:
                    decisions.Add(BeatLines.Compose($"{new BeatLines.Nm(names.Decision(open.Call.DecisionId))}: called in by {new BeatLines.Nm(names.Crew(open.Call.LeadId))}; the captain answered {BeatLines.Words(answer.Answer)} ({BeatLines.Words(answer.Standing)})."));
                    open = null;
                    break;
                case InitiativeBeat initiative:
                    decisions.Add(BeatLines.Compose($"{new BeatLines.Nm(names.Decision(initiative.DecisionId))}: {new BeatLines.Nm(names.Crew(initiative.LeadId))} chose {BeatLines.Words(initiative.Chose)} without calling{(initiative.Blocked ? ", comms blocked" : string.Empty)}."));
                    break;
            }
        }

        return decisions;
    }
}

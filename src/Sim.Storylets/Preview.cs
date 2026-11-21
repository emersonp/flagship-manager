using Sim.Core;
using Sim.Core.Content;
using Sim.Expressions;

namespace Sim.Storylets;

// One slot that held for no one, for the editor's "What plays here" (1e part six, design 4.7): the slot, the clause as written, and
// the reason as the editor shows it ("no one aboard has trait.grieving >= 1"). With no slot and no clause: "out of tries", when each
// slot held for someone and the search stopped before it found a cast that held for all.
public sealed record SlotWhy(string Slot, string Clause, string Reason);

// A storylet or vignette cast for preview on some state (design 4.7). RequiresHolds and RequiresFailed: whether requires held, and if
// not, its first failing clause and what it read ("chronicle.count('death', 120) >= 2 is 1"). The cooldowns: whether each is clear by
// the state's arrivals, read as the module reads them. Cast: each slot to a crew member, empty for the ship, null when no one fits
// (and the preview was not asked for anyway). Why: for no cast, each slot that held for no one. OptionOffered: each option, by id,
// and whether it is open to the cast. Handler: for a storylet with a check option, the sample's pick of who may be sent.
// Unconditioned: the cast was filled from the roster in order, ignoring the conditions.
public sealed record PreviewCast(
    bool RequiresHolds,
    string? RequiresFailed,
    bool CastCooldownClear,
    bool GlobalCooldownClear,
    IReadOnlyDictionary<string, string>? Cast,
    IReadOnlyList<SlotWhy> Why,
    IReadOnlyDictionary<string, bool> OptionOffered,
    string? Handler,
    bool Unconditioned);

// The editor's preview entry point (1e part six, design 4.7): cast one definition on any IStateSource (a test ship's, or the
// game's) with Casting.Cast, the module's own search, and say why when no one fits. Review focus 3: it never claims a cast the
// module's casting would refuse. Each sample casts with its own stream, made from "preview", the id, and the sample, so nothing draws
// from a legacy's streams. The definition is the pack's item with this id: the editor builds a candidate pack with the form's item in
// it.
public static class Preview
{
    public const string OutOfTries = "out of tries";

    public static PreviewCast Storylet(ContentPack pack, string id, IStateSource state, IReadOnlyList<string> roster, int sample, bool anyway)
    {
        StoryletDef storylet = pack.Storylets.FirstOrDefault(s => s.Id == id) ?? throw new ArgumentException($"The pack has no storylet {id}.", nameof(id));
        return Of(pack, id, storylet.Cast, storylet.Options, storylet.Cooldown, state, roster, sample, anyway);
    }

    public static PreviewCast Vignette(ContentPack pack, string id, IStateSource state, IReadOnlyList<string> roster, int sample, bool anyway)
    {
        VignetteDef vignette = pack.Vignettes.FirstOrDefault(v => v.Id == id) ?? throw new ArgumentException($"The pack has no vignette {id}.", nameof(id));
        return Of(pack, id, vignette.Cast, vignette.Options, pack.CooldownOf(vignette), state, roster, sample, anyway);
    }

    // The stream for one sample: the module's casting stream is cast:<id>:<cycle> from the legacy's seed; this is neither.
    internal static RngStream Stream(string id, int sample)
    {
        return RngStream.For(0, 0, $"preview:{id}:{sample}");
    }

    private static PreviewCast Of(ContentPack pack, string id, IReadOnlyList<CastSlot> slots, IReadOnlyList<StoryletOption> options, Cooldown cooldown, IStateSource state,
        IReadOnlyList<string> roster, int sample, bool anyway)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sample);
        var casting = new Casting(pack);

        // The state's arrivals say only when it last arrived, with any cast, so a cast cooldown that holds holds for every cast
        // (it may say "held back" of a cast that has never arrived; it never says "clear" of one the module would hold back).
        Since? since = state.StoryletSince(id);
        bool castClear = since is not { } c || !Salience.InCooldown(c.Cycles, cooldown.Cast);
        bool globalClear = since is not { } g || !Salience.InCooldown(g.Cycles, cooldown.Global);

        IReadOnlyDictionary<string, string>? cast = Sampled(casting, id, state, roster, pack.CastTries, sample);
        bool requiresHolds = true;
        string? requiresFailed = null;
        var why = new List<SlotWhy>();
        bool unconditioned = false;
        if (cast is null)
        {
            IReadOnlyList<Failure> failures = casting.Failures(id, state, roster);
            foreach (Failure failure in failures)
            {
                FailedClause failed = failure.Clause;
                if (failed.Slot == FailedClause.Requires)
                {
                    requiresHolds = false;
                    requiresFailed = $"{failed.Clause} is {casting.Reading(failure, state)}";
                }
                else
                {
                    why.Add(new SlotWhy(failed.Slot, failed.Clause ?? "", Reason(failed)));
                }
            }

            if (failures.Count == 0)
            {
                why.Add(new SlotWhy("", "", OutOfTries));
            }

            if (anyway && roster.Count >= slots.Count)
            {
                cast = slots.Select((s, k) => (s.Name, roster[k])).ToDictionary(p => p.Name, p => p.Item2, StringComparer.Ordinal);
                unconditioned = true;
            }
        }

        Dictionary<string, bool> offered = cast is null ? [] : options.ToDictionary(o => o.Id, o => casting.Open(id, o, state, cast), StringComparer.Ordinal);
        string? handler = null;
        if (cast is not null && options.Any(o => o.Check is not null))
        {
            List<string> fit = roster.Where(crewId => state.IsAboard(crewId) && state.IsFit(crewId) && !cast.Values.Contains(crewId)).ToList();
            handler = fit.Count == 0 ? null : fit[sample % fit.Count];
        }

        return new PreviewCast(requiresHolds, requiresFailed, castClear, globalClear, cast, why, offered, handler, unconditioned);
    }

    // Sample 0 is Casting.Cast on its stream. Each later sample casts on its own stream and passes over the casts the samples before
    // it found, as the module passes over a cast in its cooldown, so "Next sample" always moves to another cast that fits; when no
    // other fits, the samples go round again.
    private static Dictionary<string, string>? Sampled(Casting casting, string id, IStateSource state, IReadOnlyList<string> roster, int tries, int sample)
    {
        var found = new List<Dictionary<string, string>>();
        for (int k = 0; k <= sample; k++)
        {
            int n = k;
            CastResult result = casting.Cast(id, state, roster, () => Stream(id, n), tries, cast => found.Any(f => Salience.Same(f, cast)));
            if (result.Cast is not { } bound)
            {
                return found.Count == 0 ? null : found[sample % found.Count];
            }

            found.Add(bound);
        }

        return found[sample];
    }

    // "no one aboard has trait.grieving >= 1": the clause without its slot's name in front, which the slot says.
    private static string Reason(FailedClause failed)
    {
        if (failed.Clause is not { } clause)
        {
            return "too many casts to search";
        }

        string prefix = failed.Slot + ".";
        string shown = clause.StartsWith(prefix, StringComparison.Ordinal) ? clause[prefix.Length..] : clause;
        return failed.Before is { } before
            ? $"no one aboard has {shown}, though the clauses before it held for {before}"
            : $"no one aboard has {shown}";
    }
}

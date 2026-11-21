using System.Globalization;
using Microsoft.AspNetCore.Http;
using MissionBench;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;
using Web.Bridge;

namespace Web.Tuning;

// What a replay asks (design 4.9): the seed, as typed, as text; the saved or the draft tuning; and, for a sweep, which strength's team
// (empty otherwise, when the inputs name the team).
public sealed record ReplayAsk(string Seed, bool Draft, string Strength)
{
    public const string SeedName = "replay-seed";
    public const string TuningName = "replay-tuning";
    public const string StrengthName = "replay-strength";

    public static readonly IReadOnlyList<string> Strengths = ["rookie", "mixed", "veteran"];

    // The ask in a query or a form, or null when it names no seed.
    public static ReplayAsk? From(IQueryCollection query)
    {
        if (!query.ContainsKey(SeedName))
        {
            return null;
        }

        string strength = query[StrengthName].ToString();
        return new ReplayAsk(query[SeedName].ToString(), query[TuningName] == "draft", Strengths.Contains(strength) ? strength : "");
    }

    public static ReplayAsk? From(IFormCollection form)
    {
        return From(new QueryCollection(form.ToDictionary(f => f.Key, f => f.Value)));
    }

    // The ask as a query's parts, escaped, with no leading separator.
    public string Query()
    {
        string query = $"{SeedName}={Uri.EscapeDataString(Seed)}&{TuningName}={(Draft ? "draft" : "saved")}";
        return Strength.Length > 0 ? $"{query}&{StrengthName}={Strength}" : query;
    }
}

// One member's fate in a replay, as the aftermath tells it: the member's id and the name shown, their status at the end, and their light
// injuries.
public sealed record ReplayFate(string Id, string Name, MemberStatus Status, int Injuries)
{
    public bool Harmed => Status != MemberStatus.Active || Injuries > 0;
}

// A replayed run (design 4.9): its trace; the strength of a generated team (null for a real team) and the skills its levels were drawn
// from; whether it ran under the draft; each member's fate, in the team's order; and the log as play draws it.
public sealed record ReplayRun(
    MissionTrace Trace,
    TeamStrength? Strength,
    (int Low, int High)? Skills,
    bool Draft,
    IReadOnlyList<ReplayFate> Fates,
    IReadOnlyList<LogLine> Log)
{
    public bool Generated => Strength is not null;
}

// The Replay well as the page draws it: the inputs replayed, the ask, whether the inputs are a sweep (so the ask chooses the strength),
// an error by field (the seed, the tuning, or an input the bench cannot take), the launch's refusal of a real team, and the run, once
// there is one.
public sealed record ReplayView(
    TuningQuery Query,
    ReplayAsk Ask,
    IReadOnlyDictionary<string, string> Errors,
    string? Refused,
    ReplayRun? Run)
{
    public bool Sweep => !Query.RealTeam && Query.Strength == "sweep";
}

// Replay as the report (1f part one, Task 12; design 4.9): one bench run, named by its seed, drawn as play draws a mission. The run is
// Bench.Replay on the page's inputs and the seed, under the saved or the draft tuning, so it is exactly that run of the figures. Its
// beats are wrapped as the missions module publishes them (MissionBeat), and the log is the bridge's own (BridgeReader.Lines over
// Prose), with the bench's seed in place of the legacy's. Nothing is sent, published, or stored; a real team is built by BenchTeams,
// which only reads.
public static class BenchReplay
{
    // The mission's id in the prose's keys, in place of a legacy's "m_2_1".
    public const string MissionId = "bench";

    // A replay before any is asked for: the seed the inputs start from, under the draft when there is one, a sweep's rookies.
    public static ReplayView Empty(TuningQuery query, bool hasDraft)
    {
        return new ReplayView(query, new ReplayAsk(query.Seed, hasDraft, Sweeps(query) ? "rookie" : ""), new Dictionary<string, string>(), null, null);
    }

    // The replay these inputs and this ask give: the ask and the inputs judged (the inputs in the bench's words, as Run judges them), a
    // real team built once by the launch's own code, and the run under the chosen tuning.
    public static async Task<ReplayView> Ask(TuningRuns runs, TuningDrafts drafts, TuningQuery query, ReplayAsk ask, CancellationToken cancellation)
    {
        query = query with { View = "one" };
        ask = ask with { Strength = Sweeps(query) ? (ask.Strength.Length > 0 ? ask.Strength : "rookie") : "" };
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!ulong.TryParse(ask.Seed, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            errors[ReplayAsk.SeedName] = $"Seed must be a whole number of 0 or more, not '{ask.Seed}'.";
        }

        if (ask.Draft && drafts.Current(TuningDrafts.Missions) is null)
        {
            errors[ReplayAsk.TuningName] = "There is no draft. Change a number to begin one, or replay under the saved tuning.";
        }

        if (errors.Count > 0)
        {
            return new ReplayView(query, ask, errors, null, null);
        }

        RunPrepared prepared = await runs.Prepare(query, savedOnly: !ask.Draft, cancellation);
        if (prepared.Plan is not { } plan || prepared.Names is not { } names)
        {
            foreach ((string name, string error) in prepared.Errors)
            {
                errors[name] = error;
            }

            if (errors.Count == 0 && prepared.Refused is null)
            {
                errors[ReplayAsk.TuningName] = "The draft does not load, so it cannot replay. Fix the field marked in the draft, or replay under the saved tuning.";
            }

            return new ReplayView(query, ask, errors, prepared.Refused, null);
        }

        ContentPack pack = ask.Draft ? plan.Draft! : plan.Saved;
        BenchOptions options = plan.Options;
        if (ask.Strength.Length > 0)
        {
            options = options with { Strength = Enum.Parse<TeamStrength>(ask.Strength, ignoreCase: true) };
        }

        return new ReplayView(query, ask, errors, null, Replay(pack, names, options, seed, ask.Draft));
    }

    // One run under this pack's tuning, drawn as play draws it. A generated team's members are named by their place on the team (the
    // author's answer 1 to the mock-up), since their ids would name the game's crew, and take the pack's default pronouns; a real team
    // keeps its names.
    public static ReplayRun Replay(ContentPack pack, Names names, BenchOptions options, ulong seed, bool draft)
    {
        MissionTrace trace = Bench.Replay(pack, pack.Tuning, options, seed);
        bool generated = options.Team is null;
        Names shown = generated ? names.WithCrew(Places(trace.Team)) : names;
        var mission = new MissionInFlight { Id = MissionId, ObjectiveId = options.ObjectiveId, State = trace.State, BeatCount = trace.Beats.Count };
        List<MissionBeat> beats = trace.Beats.Select((beat, i) => new MissionBeat(MissionId, i, beat)).ToList();
        IReadOnlyList<LogLine> log = Log(generated ? pack with { Crew = [] } : pack, shown, seed, mission, beats, trace.Team.LeadId);
        List<ReplayFate> fates = trace.Team.Members
            .Select(m => trace.Result.Crew.FirstOrDefault(c => c.Id == m.Id) is { } result
                ? new ReplayFate(m.Id, shown.Crew(m.Id), result.Status, result.Injury)
                : new ReplayFate(m.Id, shown.Crew(m.Id), MemberStatus.Active, 0))
            .ToList();
        TeamStrength? strength = generated ? options.Strength : null;
        return new ReplayRun(trace, strength, strength is { } s ? Skills(pack.Bench, s) : null, draft, fates, log);
    }

    // The mission's log as the bridge draws it, from its beats: the bridge's own lines and prose (BridgeReader.Lines), with this seed
    // in the prose's keys.
    public static IReadOnlyList<LogLine> Log(ContentPack pack, Names names, ulong seed, MissionInFlight mission, IReadOnlyList<MissionBeat> beats, string startLeadId)
    {
        return BridgeReader.Lines(pack, beats, names, new Prose(pack, names, seed, mission, beats, startLeadId));
    }

    // A generated team named by place: the lead (always the first), then Member 2, Member 3, and on.
    public static IReadOnlyDictionary<string, string> Places(Team team)
    {
        return team.Members.Select((m, i) => (m.Id, Name: i == 0 ? "Lead" : $"Member {i + 1}")).ToDictionary(p => p.Id, p => p.Name, StringComparer.Ordinal);
    }

    // Where a worst seed's link goes: the replay route, with the run's inputs and the ask in its address (so it replays that run, whatever
    // the inputs are now); and, without script, the page with the replay shown.
    public static string Post(TuningQuery query, ReplayAsk ask)
    {
        return "/dev/tuning/replay" + With(query, ask);
    }

    public static string Page(TuningQuery query, ReplayAsk ask)
    {
        return TuningQuery.Path + With(query, ask) + "#bench-replay";
    }

    private static string With(TuningQuery query, ReplayAsk ask)
    {
        string inputs = (query with { View = "one" }).Query();
        return inputs.Length == 0 ? "?" + ask.Query() : inputs + "&" + ask.Query();
    }

    private static bool Sweeps(TuningQuery query) => !query.RealTeam && query.Strength == "sweep";

    // The skill levels a generated team of this strength is drawn from, seniors and the rest together.
    private static (int, int) Skills(BenchTuning bench, TeamStrength strength)
    {
        BenchTeamDef def = strength switch
        {
            TeamStrength.Rookie => bench.Rookie,
            TeamStrength.Veteran => bench.Veteran,
            _ => bench.Mixed,
        };
        if (def.Seniors >= def.Size)
        {
            return (def.SeniorSkillMin, def.SeniorSkillMax);
        }

        return def.Seniors > 0
            ? (Math.Min(def.SkillMin, def.SeniorSkillMin), Math.Max(def.SkillMax, def.SeniorSkillMax))
            : (def.SkillMin, def.SkillMax);
    }
}

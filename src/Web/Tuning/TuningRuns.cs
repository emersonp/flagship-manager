using MissionBench;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;
using Web.Bridge;

namespace Web.Tuning;

// Why a run stopped before its end: a new run on the same page, a change of input, a change of the draft (or its discard or save), or
// the page left (its stream closed).
public enum RunStop
{
    NewRun,
    Inputs,
    Draft,
    Left,
}

// One strength's columns (a real team's, with Strength null): the saved tuning's figures, and the draft's when there is a draft.
public sealed record RunGroup(TeamStrength? Strength, BenchFigures Saved, BenchFigures? Draft);

// A run's figures so far: Done runs of each column, of Runs, from Seed. Every column has run the same seeds, Seed to Seed + Done - 1.
public sealed record RunSnapshot(int Done, int Runs, ulong Seed, IReadOnlyList<RunGroup> Groups)
{
    public bool Finished => Done == Runs;

    public bool HasDraft => Groups.Count > 0 && Groups[0].Draft is not null;
}

// One objective's row in Every objective (design 4.8): its groups as a run of that objective gives them (a strength each, or the real
// team's), or, with no groups, the launch's refusal of the real team's size for it.
public sealed record ObjectiveRow(string ObjectiveId, IReadOnlyList<RunGroup>? Groups, string? Refused);

// Every objective's run so far (1f part one, Task 11): the rows finished, in the pack's order, of the pack's Objectives; Runs runs in
// each cell from Seed; the groups each row has (a strength each, or one null for the real team, Team); and whether there is a draft
// column. A row is added only once every cell of it, saved and draft, has all its runs, so a run stopped part way shows whole rows.
public sealed record EverySnapshot(
    IReadOnlyList<ObjectiveRow> Rows,
    IReadOnlyList<ObjectiveDef> Objectives,
    int Runs,
    ulong Seed,
    IReadOnlyList<TeamStrength?> Groups,
    bool HasDraft,
    Team? Team)
{
    public bool Finished => Rows.Count == Objectives.Count;
}

// What a run benches: the saved pack, and the pack with the draft's tuning and bench teams put in (or null with no draft); the options;
// and the groups to run, a strength each for a generated team (three for a sweep), or one null for the real team in Options.Team.
public sealed record RunPlan(ContentPack Saved, ContentPack? Draft, BenchOptions Options, IReadOnlyList<TeamStrength?> Groups);

// A run ready to start, with the live pack's names for its figures; or, with no plan, why not: an error beside each input the bench
// cannot take, a refusal of the run as a whole (the launch's words for a real team), or neither when the draft does not load.
public sealed record RunPrepared(RunPlan? Plan, Names? Names, IReadOnlyDictionary<string, string> Errors, string? Refused);

// One page's run in progress: cancelled by the next run on the page, a change of input, a change of the draft, or the page leaving.
public sealed class RunTicket(string page, CancellationTokenSource source)
{
    public string Page { get; } = page;

    public CancellationToken Token => Source.Token;

    // Why it was cancelled, or null while it runs (or when its stream closed, which is RunStop.Left).
    public RunStop? Stop { get; internal set; }

    internal CancellationTokenSource Source { get; } = source;
}

// The Tuning page's runs (1f part one, Task 10; design 4.7): one per page, named by the page's id (a signal the page holds), each with
// its own cancellation. Nothing runs by itself; a run is the bench's own Replay and BenchTally, the saved and draft columns interleaved
// seed by seed, so a run stopped half way still compares the same seeds in both.
public sealed class TuningRuns : IDisposable
{
    // A run's figures are patched every PatchEvery runs of each column or every PatchInterval, whichever is later (design 4.7).
    public const int PatchEvery = 500;

    public static readonly TimeSpan PatchInterval = TimeSpan.FromMilliseconds(250);

    private readonly object gate = new();
    private readonly Dictionary<string, RunTicket> running = new(StringComparer.Ordinal);
    private readonly TuningDrafts drafts;
    private readonly ContentHost host;
    private readonly BenchTeams teams;

    public TuningRuns(TuningDrafts drafts, ContentHost host, BenchTeams teams)
    {
        this.drafts = drafts;
        this.host = host;
        this.teams = teams;
        drafts.Changed += DraftChanged;
    }

    // The plan for a run of these inputs on the live pack (design 4.4 to 4.7): the inputs judged in the command line's words; a real
    // team built once, by the launch's own code (BenchTeams), and used for every run; and, unless savedOnly, the draft's tuning and bench
    // teams put into the pack for the draft's column (pack with { Tuning, Bench }, Task 1, Step 3, so an edit to bench.teams is
    // measured). No plan when an input is refused, the launch refuses the team, or the draft does not load.
    public async Task<RunPrepared> Prepare(TuningQuery query, bool savedOnly, CancellationToken cancellation)
    {
        if (host.Current is not { } live)
        {
            return new RunPrepared(null, null, new Dictionary<string, string>(), "No content pack is live.");
        }

        TuningInputs read = query.Read(live.Pack);
        var errors = new Dictionary<string, string>(read.Errors, StringComparer.Ordinal);
        Team? team = null;
        string? refused = null;
        if (query.RealTeam && query.Source.Length == 0)
        {
            errors["source"] = query.Team == BenchTeams.LegacyKind ? "Choose a legacy." : "Choose a test ship.";
        }
        else if (query.RealTeam && errors.Count == 0)
        {
            (team, refused) = await teams.Build(query.Team, query.Source, query.Crew, query.LeadOrFirst(), cancellation);
        }

        if (errors.Count > 0 || refused is not null)
        {
            return new RunPrepared(null, null, errors, refused);
        }

        ContentPack? draft = null;
        if (!savedOnly && drafts.Current(TuningDrafts.Missions) is not null)
        {
            DraftLoad load = drafts.Load(TuningDrafts.Missions);
            if (load.Tuning is null)
            {
                return new RunPrepared(null, null, errors, null);
            }

            draft = live.Pack with { Tuning = load.Tuning, Bench = load.Bench! };
        }

        IReadOnlyList<TeamStrength?> groups = query.RealTeam ? [null] : read.Strengths.Select(s => (TeamStrength?)s).ToList();
        return new RunPrepared(new RunPlan(live.Pack, draft, read.Options! with { Team = team }, groups), live.Names, errors, null);
    }

    // Starts the page's run, cancelling the one it had. The ticket is also cancelled when aborted is (the page's stream closed).
    public RunTicket Begin(string page, CancellationToken aborted)
    {
        var ticket = new RunTicket(page, CancellationTokenSource.CreateLinkedTokenSource(aborted));
        RunTicket? before;
        lock (gate)
        {
            running.TryGetValue(page, out before);
            running[page] = ticket;
        }

        if (before is not null)
        {
            Stop(before, RunStop.NewRun);
        }

        return ticket;
    }

    // Cancels the page's run, if it has one.
    public void Cancel(string page, RunStop why)
    {
        RunTicket? ticket;
        lock (gate)
        {
            running.TryGetValue(page, out ticket);
        }

        if (ticket is not null)
        {
            Stop(ticket, why);
        }
    }

    // The run has ended: the page no longer has it, unless a newer run has taken its place.
    public void End(RunTicket ticket)
    {
        lock (gate)
        {
            if (running.TryGetValue(ticket.Page, out RunTicket? now) && now == ticket)
            {
                running.Remove(ticket.Page);
            }
        }

        ticket.Source.Dispose();
    }

    // Whether the page has a run going.
    public bool Running(string page)
    {
        lock (gate)
        {
            return running.ContainsKey(page);
        }
    }

    public void Dispose()
    {
        drafts.Changed -= DraftChanged;
    }

    // Runs the plan's columns seed by seed: for each seed, one Replay and BenchTally.Add on every column, saved then draft, group by group.
    // Each column is the figures Bench.Run gives on the same inputs, since it is the same Replay over the same seeds into the same tally.
    // progress is given the figures so far every PatchEvery runs and interval, whichever is later, and never the last; the last (all the
    // runs, or as far as the run got when cancelled) is returned. Cancellation is checked before every seed, so it never splits one.
    public static RunSnapshot Run(RunPlan plan, Action<RunSnapshot>? progress, TimeProvider clock, TimeSpan interval, CancellationToken cancellation)
    {
        BenchOptions options = plan.Options;
        var columns = plan.Groups.Select(g => (
            Strength: g,
            Options: g is { } strength ? options with { Strength = strength } : options,
            Saved: new BenchTally(plan.Saved),
            Draft: plan.Draft is null ? null : new BenchTally(plan.Draft))).ToList();
        int done = 0;
        int patched = 0;
        long patchedAt = clock.GetTimestamp();
        while (done < options.Runs && !cancellation.IsCancellationRequested)
        {
            ulong seed = options.Seed + (ulong)done;
            foreach (var column in columns)
            {
                column.Saved.Add(Bench.Replay(plan.Saved, plan.Saved.Tuning, column.Options, seed));
                column.Draft?.Add(Bench.Replay(plan.Draft!, plan.Draft!.Tuning, column.Options, seed));
            }

            done++;
            if (progress is not null && done < options.Runs && done - patched >= PatchEvery && clock.GetElapsedTime(patchedAt) >= interval)
            {
                progress(Snapshot());
                patched = done;
                patchedAt = clock.GetTimestamp();
            }
        }

        return Snapshot();

        RunSnapshot Snapshot()
        {
            return new RunSnapshot(done, options.Runs, options.Seed, columns.Select(c => new RunGroup(c.Strength, c.Saved.Figures(), c.Draft?.Figures())).ToList());
        }
    }

    // Every objective in the saved pack's order (design 4.8, "Every objective"): each a run of the plan's columns as Run gives it, the
    // saved and draft interleaved seed by seed, with the plan's objective put in. A real team whose size an objective does not take is
    // refused there in the launch's words (LaunchTeam.SizeRefusal, as Bench.Objectives does), and nothing runs for it. Each cell is so
    // Bench.Run for that objective and strength, and the saved cells are Bench.Objectives' own. progress is given the rows so far as
    // each row finishes, but the last; the last (every row, or the rows finished when cancelled) is returned. A row cancelled part way
    // is dropped, so every row shown has all its runs in every column.
    public static EverySnapshot RunEvery(RunPlan plan, Action<EverySnapshot>? progress, CancellationToken cancellation)
    {
        IReadOnlyList<ObjectiveDef> objectives = plan.Saved.Objectives;
        var rows = new List<ObjectiveRow>();
        foreach (ObjectiveDef objective in objectives)
        {
            if (cancellation.IsCancellationRequested)
            {
                break;
            }

            if (plan.Options.Team is { } team && LaunchTeam.SizeRefusal(objective, team.Members.Count) is { } refused)
            {
                rows.Add(new ObjectiveRow(objective.Id, null, refused));
            }
            else
            {
                RunSnapshot row = Run(plan with { Options = plan.Options with { ObjectiveId = objective.Id } }, null, TimeProvider.System, TimeSpan.Zero, cancellation);
                if (!row.Finished)
                {
                    break;
                }

                rows.Add(new ObjectiveRow(objective.Id, row.Groups, null));
            }

            if (rows.Count < objectives.Count)
            {
                progress?.Invoke(EveryStart(plan) with { Rows = [.. rows] });
            }
        }

        return EveryStart(plan) with { Rows = rows };
    }

    // Every objective before its first row.
    public static EverySnapshot EveryStart(RunPlan plan)
    {
        return new EverySnapshot([], plan.Saved.Objectives, plan.Options.Runs, plan.Options.Seed, plan.Groups, plan.Draft is not null, plan.Options.Team);
    }

    // A run before its first seed: no runs in any column.
    public static RunSnapshot Start(RunPlan plan)
    {
        return new RunSnapshot(0, plan.Options.Runs, plan.Options.Seed, plan.Groups.Select(g => new RunGroup(g, new BenchTally(plan.Saved).Figures(), plan.Draft is null ? null : new BenchTally(plan.Draft).Figures())).ToList());
    }

    private void DraftChanged(string file)
    {
        List<RunTicket> all;
        lock (gate)
        {
            all = [.. running.Values];
        }

        foreach (RunTicket ticket in all)
        {
            Stop(ticket, RunStop.Draft);
        }
    }

    private static void Stop(RunTicket ticket, RunStop why)
    {
        lock (ticket)
        {
            ticket.Stop ??= why;
        }

        try
        {
            ticket.Source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It ended as it was stopped.
        }
    }
}

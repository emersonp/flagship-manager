using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Persistence;
using Sim.Core;
using Sim.Missions;
using Web.Bridge;
using Web.Components;

namespace Web.Endpoints;

public static class BridgeEndpoints
{
    private const string Incomplete = "The form was not complete.";

    private const string OutOfDate = "This page was out of date. It has been refreshed.";

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/legacy/{id}", Page);
        MapActions(app.MapGroup("/legacy"), dev: false);
    }

    // The bridge's actions under a prefix: /legacy for play, and /dev/legacy for Dev Play, which is mapped only in developer mode.
    // The route alone says whether the answer is drawn for Dev Play; nothing the browser sends can ask for it.
    internal static void MapActions(RouteGroupBuilder group, bool dev)
    {
        group.MapPost("/{id}/advance", (string id, HttpContext http, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation)
            => Advance(id, http, game, sessions, render, dev, cancellation));
        group.MapPost("/{id}/launch", (string id, HttpContext http, GameServices game, LegacySession sessions, Render render, IOptions<WebSettings> settings, CancellationToken cancellation)
            => Launch(id, http, game, sessions, render, settings, dev, cancellation));
        group.MapPost("/{id}/answer", (string id, HttpContext http, GameServices game, LegacySession sessions, Render render, IOptions<WebSettings> settings, CancellationToken cancellation)
            => Answer(id, http, game, sessions, render, settings, dev, cancellation));
        group.MapPost("/{id}/storylet", (string id, HttpContext http, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation)
            => AnswerItinerary(id, http, game, sessions, render, (offer, option, handler) => new AnswerStorylet(offer, option, handler), dev, cancellation));
        group.MapPost("/{id}/vignette", (string id, HttpContext http, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation)
            => AnswerItinerary(id, http, game, sessions, render, (offer, option, _) => new AnswerVignette(offer, option), dev, cancellation));
        if (dev)
        {
            // Dev Play alone turns a cycle as a journey of N weeks (slice 1e-calendar); play never offers it.
            group.MapPost("/{id}/travel", (string id, HttpContext http, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation)
                => Travel(id, http, game, sessions, render, cancellation));
        }
    }

    private static async Task<IResult> Page(string id, HttpContext http, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation)
    {
        LivePack live = RequestPack.Of(http);
        LoadedLegacy? legacy = Guid.TryParse(id, out Guid legacyId) ? await sessions.Read(live, legacyId, cancellation) : null;
        if (legacy is null)
        {
            return await NotFound(render, http);
        }

        string html = await render.Html<BridgePage>(new Dictionary<string, object?> { ["LegacyId"] = legacyId, ["View"] = View(game, live, legacy, false) });
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> Advance(string id, HttpContext http, GameServices game, LegacySession sessions, Render render, bool dev, CancellationToken cancellation)
    {
        Stamp? stamp = StampOf(await ReadForm(http.Request, cancellation));
        LivePack live = RequestPack.Of(http);
        return await Send(id, http, game, live, sessions, render, dev, legacy =>
        {
            Check(stamp, dev, game, live, legacy);
            // Aftermath goes on through the advance phase, so one request starts the next cycle.
            bool fromAftermath = legacy.State.Phase == CyclePhase.Aftermath;
            try
            {
                legacy.Send(new AdvancePhase());
                if (fromAftermath)
                {
                    legacy.Send(new AdvancePhase());
                }
            }
            catch (CommandRejectedException rejected)
            {
                throw new RefusedException(rejected.Message, RefusedStatus(http));
            }
        }, cancellation);
    }

    // As Advance from the aftermath or the advance phase, but the turn is Travel(weeks): one roll times N. The cycle module guards
    // the phase and the weeks, so a refusal reads as its message.
    private static async Task<IResult> Travel(string id, HttpContext http, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation)
    {
        IFormCollection? form = await ReadForm(http.Request, cancellation);
        Stamp? stamp = StampOf(form);
        string? weeks = form is null ? null : Single(form, "weeks");
        LivePack live = RequestPack.Of(http);
        return await Send(id, http, game, live, sessions, render, true, legacy =>
        {
            Check(stamp, true, game, live, legacy);
            try
            {
                if (!int.TryParse(weeks, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                {
                    throw new CommandRejectedException($"A journey is 1 to {Sim.Core.Travel.Most} weeks.");
                }

                if (legacy.State.Phase == CyclePhase.Aftermath)
                {
                    legacy.Send(new AdvancePhase());
                }

                legacy.Send(new Sim.Core.Travel(n));
            }
            catch (CommandRejectedException rejected)
            {
                throw new RefusedException(rejected.Message, RefusedStatus(http));
            }
        }, cancellation);
    }

    private static async Task<IResult> Launch(string id, HttpContext http, GameServices game, LegacySession sessions, Render render, IOptions<WebSettings> settings, bool dev, CancellationToken cancellation)
    {
        IFormCollection? form = await ReadForm(http.Request, cancellation);
        Stamp? stamp = StampOf(form);
        AssignTeam? team = Bind(form);
        LivePack live = RequestPack.Of(http);
        return await SendPaced<Refusal>(id, http, game, live, sessions, render, settings.Value.BeatDelayMs, dev, legacy =>
        {
            Check(stamp, dev, game, live, legacy);
            if (team is null)
            {
                throw new CommandRejectedException(Incomplete);
            }

            legacy.Send(team);
            legacy.Send(new AdvancePhase());
        }, cancellation);
    }

    private static async Task<IResult> Answer(string id, HttpContext http, GameServices game, LegacySession sessions, Render render, IOptions<WebSettings> settings, bool dev, CancellationToken cancellation)
    {
        IFormCollection? form = await ReadForm(http.Request, cancellation);
        Stamp? stamp = StampOf(form);
        AnswerCall? answer = BindAnswer(form);
        LivePack live = RequestPack.Of(http);
        return await SendPaced<CallRefusal>(id, http, game, live, sessions, render, settings.Value.BeatDelayMs, dev, legacy =>
        {
            Check(stamp, dev, game, live, legacy);
            if (answer is null)
            {
                throw new CommandRejectedException(Incomplete);
            }

            legacy.Send(answer);
        }, cancellation);
    }

    // A storylet's or a vignette's answer. Any refusal redraws the page from the stored state, with the reason beside the
    // itinerary's panels when they are on screen, else in the bar. Handler: who is sent on a storylet's check (1e part four, Task 6);
    // empty or absent is none, and the module refuses it where it does not belong. A vignette takes none.
    private static async Task<IResult> AnswerItinerary(string id, HttpContext http, GameServices game, LegacySession sessions, Render render, Func<string, string, string?, ICommand> command, bool dev, CancellationToken cancellation)
    {
        IFormCollection? form = await ReadForm(http.Request, cancellation);
        Stamp? stamp = StampOf(form);
        string? offer = form is null ? null : Single(form, "offer");
        string? option = form is null ? null : Single(form, "option");
        string? handler = form is null ? null : Single(form, "handler") is { Length: > 0 } named ? named : null;
        LivePack live = RequestPack.Of(http);
        if (!Guid.TryParse(id, out Guid legacyId))
        {
            return await NotFound(render, http);
        }

        BridgeView? view;
        try
        {
            view = await sessions.Run(live, legacyId, legacy =>
            {
                Check(stamp, dev, game, live, legacy);
                if (string.IsNullOrEmpty(offer) || string.IsNullOrEmpty(option))
                {
                    throw new CommandRejectedException(Incomplete);
                }

                legacy.Send(command(offer, option, handler));
                return View(game, live, legacy, dev);
            }, cancellation);
        }
        catch (StalePageException stale)
        {
            return await Patches(render, legacyId, stale.Current, OutOfDate);
        }
        catch (CommandRejectedException rejected)
        {
            if (await sessions.Read(live, legacyId, cancellation) is not { } current)
            {
                return await NotFound(render, http);
            }

            BridgeView corrected = View(game, live, current, dev);
            bool shown = corrected.Itinerary is { IsEmpty: false };
            return await Patches(render, legacyId, corrected, shown ? null : rejected.Message, storyletReason: shown ? rejected.Message : null, status: RefusedStatus(http));
        }

        return view is null ? await NotFound(render, http) : await Patches(render, legacyId, view, null);
    }

    // A refusal answers 422, except to Datastar itself: Datastar 1.0.5 ignores the body of any response but a 200, so the reason would
    // never reach the page.
    private static int RefusedStatus(HttpContext http)
    {
        return Sse.IsDatastar(http.Request) ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity;
    }

    // Commits first, then plays this request's new beats one at a time; the view before the commit rides along so the
    // opening patches and the held-back parts do not give the result away.
    private static async Task<IResult> SendPaced<TRefusal>(string id, HttpContext http, GameServices game, LivePack live, LegacySession sessions, Render render, int beatDelayMs, bool dev, Action<LoadedLegacy> commands, CancellationToken cancellation)
        where TRefusal : IComponent
    {
        if (!Guid.TryParse(id, out Guid legacyId))
        {
            return await NotFound(render, http);
        }

        Turn? turn;
        try
        {
            turn = await sessions.Run(live, legacyId, legacy =>
            {
                BridgeView before = View(game, live, legacy, dev);
                commands(legacy);
                return new Turn(before, View(game, live, legacy, dev));
            }, cancellation);
        }
        catch (StalePageException stale)
        {
            return await Patches(render, legacyId, stale.Current, OutOfDate);
        }
        catch (CommandRejectedException rejected)
        {
            // A refused answer may be for a call no longer on screen: redraw from the stored state, the reason in the
            // waiting call's panel or, with none waiting, in the bar, so the page never stays on a dead call.
            if (typeof(TRefusal) == typeof(CallRefusal) && await sessions.Read(live, legacyId, cancellation) is { } current)
            {
                BridgeView corrected = View(game, live, current, dev);
                return corrected.Mission?.Call is null
                    ? await Patches(render, legacyId, corrected, rejected.Message)
                    : await Patches(render, legacyId, corrected, null, rejected.Message);
            }

            string refusal = await render.Html<TRefusal>(new Dictionary<string, object?> { ["Reason"] = rejected.Message });
            return new StreamResult(async response =>
            {
                await Sse.PatchElements(response, refusal);
            });
        }

        if (turn is null)
        {
            return await NotFound(render, http);
        }

        BridgeView after = turn.After;
        MissionView? mission = after.Mission;
        int known = turn.Before.Mission?.Log.Count ?? 0;
        // A launch has no earlier mission: show an empty log, no meter, and no call until the beats have played.
        bool launching = turn.Before.Mission is null;
        // The opening screen shows the lead as it stood before this request's beats; a lead change appears with its log line and the closing patches.
        // An answer opens with the call gone: it has been answered, and the next call, if any, comes after the last beat.
        MissionView? opening = launching ? mission is null ? null : mission with { Log = [], Call = null, Outcome = null, LeadId = mission.StartLeadId } : turn.Before.Mission! with { Call = null };
        IReadOnlyList<CrewRow> openingRoster = opening is null ? turn.Before.Roster : BridgeReader.MarkAway(turn.Before.Roster, opening.Team, opening.LeadId);
        BridgeView first = after with { Mission = opening, Roster = openingRoster };

        string topBar = await render.Html<TopBar>(new Dictionary<string, object?> { ["Bar"] = after.TopBar });
        string main = await render.Html<MainScreen>(new Dictionary<string, object?> { ["LegacyId"] = legacyId, ["View"] = first, ["HideMeter"] = launching });
        string roster = await render.Html<Roster>(new Dictionary<string, object?> { ["Rows"] = openingRoster });
        // Each beat's patches: the log, then the route when that beat is a stage, so the route never gets ahead of the log.
        // An answer's meter is on screen, so a stage beat also sets it to the figures that stage line states.
        var beats = new List<string[]>();
        for (int count = known + 1; mission is not null && count <= mission.Log.Count; count++)
        {
            List<LogLine> shown = mission.Log.Take(count).ToList();
            var patches = new List<string> { await render.Html<MissionLog>(new Dictionary<string, object?> { ["Lines"] = shown, ["Dev"] = dev }) };
            if (shown[^1] is { Kind: "stage" } stage)
            {
                patches.Add(await render.Html<StageRoute>(new Dictionary<string, object?> { ["Lines"] = shown, ["Dev"] = dev }));
                if (!launching && stage is { Momentum: int momentum, Strain: int strain })
                {
                    patches.Add(await render.Html<Meter>(new Dictionary<string, object?> { ["Mission"] = mission with { Momentum = momentum, Strain = strain } }));
                }
            }

            beats.Add(patches.ToArray());
        }

        // Once the result line has been shown, the route no longer has a current stage.
        string? endedRoute = mission?.Outcome is null ? null : await render.Html<StageRoute>(new Dictionary<string, object?> { ["Lines"] = mission.Log, ["Ended"] = true, ["Dev"] = dev });
        string meter = await render.Html<Meter>(new Dictionary<string, object?> { ["Mission"] = mission });
        string call = await render.Html<CallPanel>(new Dictionary<string, object?> { ["LegacyId"] = legacyId, ["Bar"] = after.TopBar, ["MissionId"] = mission?.MissionId ?? "", ["Call"] = mission?.Call });
        string footer = await render.Html<ResolutionFooter>(new Dictionary<string, object?> { ["LegacyId"] = legacyId, ["Bar"] = after.TopBar, ["Show"] = mission is null || mission.Outcome is not null });
        string? team = mission is null ? null : await render.Html<TeamLine>(new Dictionary<string, object?> { ["Team"] = mission.Team, ["TeamNames"] = mission.TeamShown, ["LeadId"] = mission.LeadId });
        string finalRoster = await render.Html<Roster>(new Dictionary<string, object?> { ["Rows"] = after.Roster });
        return new StreamResult(async response =>
        {
            try
            {
                await Sse.PatchElements(response, topBar);
                await Sse.PatchElements(response, main);
                await Sse.PatchElements(response, roster);
                foreach (string[] beat in beats)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (beatDelayMs > 0)
                    {
                        await Task.Delay(beatDelayMs, cancellation);
                    }

                    foreach (string patch in beat)
                    {
                        await Sse.PatchElements(response, patch);
                    }
                }

                cancellation.ThrowIfCancellationRequested();
                if (endedRoute is not null)
                {
                    await Sse.PatchElements(response, endedRoute);
                }

                await Sse.PatchElements(response, meter);
                await Sse.PatchElements(response, call);
                await Sse.PatchElements(response, footer);
                if (team is not null)
                {
                    await Sse.PatchElements(response, team);
                }

                await Sse.PatchElements(response, finalRoster);
            }
            catch (Exception e) when (e is OperationCanceledException or IOException)
            {
                // The client has gone; everything is already stored and the next GET shows it all.
            }
        });
    }

    private static async Task<IResult> Patches(Render render, Guid legacyId, BridgeView view, string? reason, string? callReason = null, string? storyletReason = null, int status = StatusCodes.Status200OK)
    {
        string topBar = await render.Html<TopBar>(new Dictionary<string, object?> { ["Bar"] = view.TopBar });
        string main = await render.Html<MainScreen>(new Dictionary<string, object?> { ["LegacyId"] = legacyId, ["View"] = view, ["Reason"] = reason, ["CallReason"] = callReason, ["StoryletReason"] = storyletReason });
        string roster = await render.Html<Roster>(new Dictionary<string, object?> { ["Rows"] = view.Roster });
        return new StreamResult(async response =>
        {
            await Sse.PatchElements(response, topBar);
            await Sse.PatchElements(response, main);
            await Sse.PatchElements(response, roster);
        }, status);
    }

    private sealed record Turn(BridgeView Before, BridgeView After);

    private sealed record Stamp(int Cycle, CyclePhase Phase);

    // A refusal that answers with its own status (the bus refusing an advance).
    private sealed class RefusedException(string message, int status) : Exception(message)
    {
        public int Status { get; } = status;
    }

    // Carries the stored state's view out of the lock, so the redrawn page is the one the check saw.
    private sealed class StalePageException(BridgeView current) : Exception(OutOfDate)
    {
        public BridgeView Current { get; } = current;
    }

    // Commits the commands in one run, then answers with the three patches or, on a rejection, the refusal alone.
    private static async Task<IResult> Send(string id, HttpContext http, GameServices game, LivePack live, LegacySession sessions, Render render, bool dev, Action<LoadedLegacy> commands, CancellationToken cancellation)
    {
        if (!Guid.TryParse(id, out Guid legacyId))
        {
            return await NotFound(render, http);
        }

        BridgeView? view;
        try
        {
            view = await sessions.Run(live, legacyId, legacy =>
            {
                commands(legacy);
                return View(game, live, legacy, dev);
            }, cancellation);
        }
        catch (StalePageException stale)
        {
            return await Patches(render, legacyId, stale.Current, OutOfDate);
        }
        catch (Exception e) when (e is CommandRejectedException or RefusedException)
        {
            string refusal = await render.Html<Refusal>(new Dictionary<string, object?> { ["Reason"] = e.Message });
            return new StreamResult(async response =>
            {
                await Sse.PatchElements(response, refusal);
            }, (e as RefusedException)?.Status ?? StatusCodes.Status200OK);
        }

        if (view is null)
        {
            return await NotFound(render, http);
        }

        string topBar = await render.Html<TopBar>(new Dictionary<string, object?> { ["Bar"] = view.TopBar });
        string main = await render.Html<MainScreen>(new Dictionary<string, object?> { ["LegacyId"] = legacyId, ["View"] = view });
        string roster = await render.Html<Roster>(new Dictionary<string, object?> { ["Rows"] = view.Roster });
        return new StreamResult(async response =>
        {
            await Sse.PatchElements(response, topBar);
            await Sse.PatchElements(response, main);
            await Sse.PatchElements(response, roster);
        });
    }

    // Null when the body is not a readable form.
    internal static async Task<IFormCollection?> ReadForm(HttpRequest request, CancellationToken cancellation)
    {
        try
        {
            return await request.ReadFormAsync(cancellation);
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    // The cycle and phase the page was drawn at; null when either is missing or unparseable.
    private static Stamp? StampOf(IFormCollection? form)
    {
        string? cycle = form is null ? null : Single(form, "cycle");
        string? phase = form is null ? null : Single(form, "phase");
        // Names only for the phase: a number would parse as an enum value.
        if (cycle is null || !int.TryParse(cycle, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || phase is null || !Enum.GetNames<CyclePhase>().Contains(phase))
        {
            return null;
        }

        return new Stamp(number, Enum.Parse<CyclePhase>(phase));
    }

    // At the edge, inside the lock and before any command: a page drawn in another cycle or phase sends nothing.
    private static void Check(Stamp? stamp, bool dev, GameServices game, LivePack live, LoadedLegacy legacy)
    {
        if (stamp is null)
        {
            throw new CommandRejectedException(Incomplete);
        }

        if (stamp.Cycle != legacy.State.Cycle || stamp.Phase != legacy.State.Phase)
        {
            throw new StalePageException(View(game, live, legacy, dev));
        }
    }

    // Anything missing or unparseable is null; the bus judges the rest.
    private static AssignTeam? Bind(IFormCollection? form)
    {
        if (form is null)
        {
            return null;
        }

        string? objective = Single(form, "objective");
        string? lead = Single(form, "lead");
        if (string.IsNullOrEmpty(objective) || string.IsNullOrEmpty(lead) || !Number(form, "priority", out int priority) || !Number(form, "autonomy", out int autonomy))
        {
            return null;
        }

        return new AssignTeam(objective, form["crew"].OfType<string>().ToList(), lead, form["gear"].OfType<string>().ToList(), new Parameters(priority, autonomy));
    }

    private static AnswerCall? BindAnswer(IFormCollection? form)
    {
        if (form is null)
        {
            return null;
        }

        string? mission = Single(form, "mission");
        string? call = Single(form, "call");
        string? answer = Single(form, "answer");
        // Names only: a number would parse as an enum value.
        if (string.IsNullOrEmpty(mission) || string.IsNullOrEmpty(call) || answer is null || !Enum.GetNames<CallAnswer>().Contains(answer))
        {
            return null;
        }

        return new AnswerCall(mission, call, Enum.Parse<CallAnswer>(answer));
    }

    internal static string? Single(IFormCollection form, string key)
    {
        StringValues values = form[key];
        return values.Count == 1 ? values[0] : null;
    }

    private static bool Number(IFormCollection form, string key, out int value)
    {
        value = 0;
        return Single(form, key) is { } text && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    // This cycle's stored events plus any not yet committed, so a page built inside a run sees its own commands.
    // dev: drawn for Dev Play, so the screens show ids and links to the content editor.
    internal static BridgeView View(GameServices game, LivePack live, LoadedLegacy legacy, bool dev)
    {
        int cycle = legacy.State.Cycle;
        List<IEvent> events = CycleEvents(game, live, legacy);
        // The briefing picks the last cycle's team again; a cycle that sent nobody leaves nothing to pick.
        TeamAssigned? last = legacy.State.Phase == CyclePhase.Briefing && cycle > 0
            ? game.Store.ReadEvents(legacy.State.LegacyId, cycle - 1).Select(live.Game.Events.FromEnvelope).OfType<TeamAssigned>().LastOrDefault()
            : null;
        // The itinerary says how the turn went; the turn's roll is stamped with the cycle that ended.
        CycleLengthRolled? turned = legacy.State.Phase == CyclePhase.Itinerary && cycle > 0 ? CycleEvents(game, live, legacy, cycle - 1).OfType<CycleLengthRolled>().LastOrDefault() : null;
        BridgeView view = BridgeReader.Read(legacy.Record, live.Pack, events, live.Names, last, live.Game.Bus.View<IStoryletView>(legacy.State), turned);
        return dev ? view with { TopBar = view.TopBar with { Dev = true } } : view;
    }

    // This cycle's stored events plus any not yet committed.
    internal static List<IEvent> CycleEvents(GameServices game, LivePack live, LoadedLegacy legacy)
    {
        return CycleEvents(game, live, legacy, legacy.State.Cycle);
    }

    // A cycle's stored events plus any of its own not yet committed.
    private static List<IEvent> CycleEvents(GameServices game, LivePack live, LoadedLegacy legacy, int cycle)
    {
        List<IEvent> events = game.Store.ReadEvents(legacy.State.LegacyId, cycle).Select(live.Game.Events.FromEnvelope).ToList();
        events.AddRange(legacy.Envelopes.Where(e => e.Cycle == cycle).Select(live.Game.Events.FromEnvelope));
        return events;
    }

    // A Datastar request gets the words as a patch of the main screen; a 404 page would change nothing on screen.
    internal static async Task<IResult> NotFound(Render render, HttpContext http)
    {
        if (Sse.IsDatastar(http.Request))
        {
            string main = await render.Html<NotFoundMain>(new Dictionary<string, object?>());
            return new StreamResult(async response =>
            {
                await Sse.PatchElements(response, main);
            });
        }

        string html = await render.Html<NotFoundPage>(new Dictionary<string, object?>());
        return Results.Content(html, "text/html; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
    }

    internal sealed class StreamResult(Func<HttpResponse, Task> write, int status = StatusCodes.Status200OK) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = status;
            await Sse.Start(httpContext.Response);
            await write(httpContext.Response);
        }
    }
}

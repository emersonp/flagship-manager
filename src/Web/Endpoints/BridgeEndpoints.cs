using System.Globalization;
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

    public static void Map(WebApplication app)
    {
        app.MapGet("/legacy/{id}", Page);
        app.MapPost("/legacy/{id}/advance", Advance);
        app.MapPost("/legacy/{id}/launch", Launch);
    }

    private static async Task<IResult> Page(string id, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation)
    {
        LoadedLegacy? legacy = Guid.TryParse(id, out Guid legacyId) ? await sessions.Read(legacyId, cancellation) : null;
        if (legacy is null)
        {
            return await NotFound(render);
        }

        string html = await render.Html<BridgePage>(new Dictionary<string, object?> { ["LegacyId"] = legacyId, ["View"] = View(game, legacy) });
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> Advance(string id, HttpContext http, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation)
    {
        return await Send(id, http, game, sessions, render, legacy =>
        {
            // Aftermath goes on through the advance phase, so one request starts the next cycle.
            bool fromAftermath = legacy.State.Phase == CyclePhase.Aftermath;
            legacy.Send(new AdvancePhase());
            if (fromAftermath)
            {
                legacy.Send(new AdvancePhase());
            }
        }, cancellation);
    }

    private static async Task<IResult> Launch(string id, HttpContext http, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation)
    {
        AssignTeam? team = await Bind(http.Request, cancellation);
        return await Send(id, http, game, sessions, render, legacy =>
        {
            if (team is null)
            {
                throw new CommandRejectedException(Incomplete);
            }

            legacy.Send(team);
            legacy.Send(new AdvancePhase());
        }, cancellation);
    }

    // Commits the commands in one run, then answers with the three patches or, on a rejection, the refusal alone.
    private static async Task<IResult> Send(string id, HttpContext http, GameServices game, LegacySession sessions, Render render, Action<LoadedLegacy> commands, CancellationToken cancellation)
    {
        if (!Guid.TryParse(id, out Guid legacyId))
        {
            return await NotFound(render);
        }

        BridgeView? view;
        try
        {
            view = await sessions.Run(legacyId, legacy =>
            {
                commands(legacy);
                return View(game, legacy);
            }, cancellation);
        }
        catch (CommandRejectedException rejected)
        {
            string refusal = await render.Html<Refusal>(new Dictionary<string, object?> { ["Reason"] = rejected.Message });
            return new StreamResult(async response =>
            {
                await Sse.PatchElements(response, refusal);
            });
        }

        if (view is null)
        {
            return await NotFound(render);
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

    // Anything missing or unparseable is null; the bus judges the rest.
    private static async Task<AssignTeam?> Bind(HttpRequest request, CancellationToken cancellation)
    {
        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellation);
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException)
        {
            return null;
        }

        string? objective = Single(form, "objective");
        string? lead = Single(form, "lead");
        if (string.IsNullOrEmpty(objective) || string.IsNullOrEmpty(lead) || !Number(form, "priority", out int priority) || !Number(form, "risk", out int risk) || !Number(form, "autonomy", out int autonomy))
        {
            return null;
        }

        return new AssignTeam(objective, form["crew"].OfType<string>().ToList(), lead, form["gear"].OfType<string>().ToList(), new Parameters(priority, risk, autonomy));
    }

    private static string? Single(IFormCollection form, string key)
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
    private static BridgeView View(GameServices game, LoadedLegacy legacy)
    {
        int cycle = legacy.State.Cycle;
        List<IEvent> events = game.Store.ReadEvents(legacy.State.LegacyId, cycle).Select(game.Game.Events.FromEnvelope).ToList();
        events.AddRange(legacy.Envelopes.Where(e => e.Cycle == cycle).Select(game.Game.Events.FromEnvelope));
        return BridgeReader.Read(legacy.Record, game.Pack, events);
    }

    private static async Task<IResult> NotFound(Render render)
    {
        string html = await render.Html<NotFoundPage>(new Dictionary<string, object?>());
        return Results.Content(html, "text/html; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
    }

    private sealed class StreamResult(Func<HttpResponse, Task> write) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            await Sse.Start(httpContext.Response);
            await write(httpContext.Response);
        }
    }
}

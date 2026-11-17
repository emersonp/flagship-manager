using System.Globalization;
using Sim.Core;
using Web.Bridge;
using Web.Components;

namespace Web.Endpoints;

// Dev Play: the bridge with the developer's panel. Mapped only in developer mode, under /dev, so the one check on that prefix
// (today the setting, one day a permission) covers it. Play itself still posts to the bridge's own routes.
public static class DevPlayEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder dev = app.MapGroup("/dev/legacy").AddEndpointFilter(SameOrigin.Filter).AddEndpointFilter(RequestPack.Filter);
        dev.MapGet("/{id}", Page);
        dev.MapGet("/{id}/panel", Panel);
        dev.MapPost("/{id}/flag", Set);
    }

    private static async Task<IResult> Page(string id, HttpContext http, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation)
    {
        LivePack live = RequestPack.Of(http);
        LoadedLegacy? legacy = Guid.TryParse(id, out Guid legacyId) ? await sessions.Read(live, legacyId, cancellation) : null;
        if (legacy is null)
        {
            return await BridgeEndpoints.NotFound(render, http);
        }

        string html = await render.Html<BridgePage>(new Dictionary<string, object?>
        {
            ["LegacyId"] = legacyId,
            ["View"] = BridgeEndpoints.View(game, live, legacy),
            ["Dev"] = DevReader.Read(legacy.State, live.Game, live.Pack, live.Names),
        });
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> Panel(string id, HttpContext http, LegacySession sessions, Render render, CancellationToken cancellation)
    {
        LivePack live = RequestPack.Of(http);
        LoadedLegacy? legacy = Guid.TryParse(id, out Guid legacyId) ? await sessions.Read(live, legacyId, cancellation) : null;
        if (legacy is null)
        {
            return await BridgeEndpoints.NotFound(render, http);
        }

        return await Patch(render, legacyId, DevReader.Read(legacy.State, live.Game, live.Pack, live.Names), []);
    }

    // Answers with the panel either way: what was set, or why not, with the form as posted.
    private static async Task<IResult> Set(string id, HttpContext http, LegacySession sessions, Render render, CancellationToken cancellation)
    {
        IFormCollection? form = await BridgeEndpoints.ReadForm(http.Request, cancellation);
        string flag = (form is null ? null : BridgeEndpoints.Single(form, "flag"))?.Trim() ?? "";
        string on = (form is null ? null : BridgeEndpoints.Single(form, "on")) ?? DevReader.Ship;
        string text = (form is null ? null : BridgeEndpoints.Single(form, "value"))?.Trim() ?? "";
        LivePack live = RequestPack.Of(http);
        if (!Guid.TryParse(id, out Guid legacyId))
        {
            return await BridgeEndpoints.NotFound(render, http);
        }

        var kept = new Dictionary<string, object?> { ["Flag"] = flag, ["On"] = on, ["Value"] = text };
        string? crewId = on == DevReader.Ship ? null : on;
        string? done = null;
        string? refused = null;
        DevView? view;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            refused = $"A flag's value is 0 to {SetFlag.Most}.";
            view = await Current(live, legacyId, sessions, cancellation);
        }
        else
        {
            try
            {
                view = await sessions.Run(live, legacyId, legacy =>
                {
                    legacy.Send(new SetFlag(flag, crewId, value));
                    string where = crewId is null ? "the ship" : live.Names.Crew(crewId);
                    done = value == 0 ? $"Cleared {flag} on {where}." : $"{flag} is now {value} on {where}.";
                    return DevReader.Read(legacy.State, live.Game, live.Pack, live.Names);
                }, cancellation);
            }
            catch (CommandRejectedException rejected)
            {
                refused = rejected.Message;
                view = await Current(live, legacyId, sessions, cancellation);
            }
        }

        if (view is null)
        {
            return await BridgeEndpoints.NotFound(render, http);
        }

        kept["Message"] = refused ?? done;
        kept["Error"] = refused is not null;
        return await Patch(render, legacyId, view, kept);
    }

    private static async Task<DevView?> Current(LivePack live, Guid legacyId, LegacySession sessions, CancellationToken cancellation)
    {
        return await sessions.Read(live, legacyId, cancellation) is { } legacy ? DevReader.Read(legacy.State, live.Game, live.Pack, live.Names) : null;
    }

    private static async Task<IResult> Patch(Render render, Guid legacyId, DevView view, Dictionary<string, object?> parameters)
    {
        parameters["LegacyId"] = legacyId;
        parameters["View"] = view;
        string panel = await render.Html<DevPanel>(parameters);
        return new BridgeEndpoints.StreamResult(async response =>
        {
            await Sse.PatchElements(response, panel);
        });
    }
}

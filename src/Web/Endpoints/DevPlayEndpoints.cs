using System.Globalization;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Web.Bridge;
using Web.Components;
using Web.Forms;

namespace Web.Endpoints;

// Dev Play: the bridge with the developer's panel. Mapped only in developer mode, under /dev, so the one check on that prefix
// (today the setting, one day a permission) covers it. Its pages post the bridge's actions here too, so their answers are drawn for Dev Play.
public static class DevPlayEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder dev = app.MapGroup("/dev/legacy").AddEndpointFilter(SameOrigin.Filter).AddEndpointFilter(RequestPack.Filter);
        dev.MapGet("/{id}", Page);
        dev.MapGet("/{id}/panel", Panel);
        // The Chronicle with each entry's kind, weight, cycle, and source sequence numbers.
        dev.MapGet("/{id}/chronicle", (string id, HttpContext http, LegacySession sessions, Render render, CancellationToken cancellation)
            => BridgeEndpoints.Chronicle(id, http, sessions, render, true, cancellation));
        dev.MapPost("/{id}/trait", Set);
        dev.MapPost("/{id}/why", Why);
        dev.MapPost("/{id}/testship", SaveShip);
        // The bridge's own actions, answered for Dev Play.
        BridgeEndpoints.MapActions(dev, dev: true);
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
            ["View"] = BridgeEndpoints.View(game, live, legacy, true),
            ["Dev"] = DevReader.Read(legacy.State, live.Game, live.Pack, live.Names, BridgeEndpoints.CycleEvents(game, live, legacy)),
        });
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> Panel(string id, HttpContext http, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation)
    {
        LivePack live = RequestPack.Of(http);
        LoadedLegacy? legacy = Guid.TryParse(id, out Guid legacyId) ? await sessions.Read(live, legacyId, cancellation) : null;
        if (legacy is null)
        {
            return await BridgeEndpoints.NotFound(render, http);
        }

        return await Patch(render, legacyId, DevReader.Read(legacy.State, live.Game, live.Pack, live.Names, BridgeEndpoints.CycleEvents(game, live, legacy)), []);
    }

    // "Why these storylets" (1e part four, Task 7): a search by id or name, answered with the section alone. It reads only.
    private static async Task<IResult> Why(string id, HttpContext http, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation)
    {
        IFormCollection? form = await BridgeEndpoints.ReadForm(http.Request, cancellation);
        string query = (form is null ? null : BridgeEndpoints.Single(form, "q")) ?? "";
        LivePack live = RequestPack.Of(http);
        LoadedLegacy? legacy = Guid.TryParse(id, out Guid legacyId) ? await sessions.Read(live, legacyId, cancellation) : null;
        if (legacy is null)
        {
            return await BridgeEndpoints.NotFound(render, http);
        }

        WhyView view = WhyReader.Read(legacy.State, live.Game, live.Pack, live.Names, BridgeEndpoints.CycleEvents(game, live, legacy), query);
        string section = await render.Html<WhyStorylets>(new Dictionary<string, object?> { ["LegacyId"] = legacyId, ["View"] = view });
        return new BridgeEndpoints.StreamResult(async response =>
        {
            await Sse.PatchElements(response, section);
        });
    }

    // "Save as test ship" (1e part six, Task 14): the legacy as the expressions read it now, appended to the test ships through the
    // editor's usual save. It reads the legacy and writes only the content file; the panel says where the ship went, or why not.
    private static async Task<IResult> SaveShip(string id, HttpContext http, GameServices game, LegacySession sessions, ContentEditor editor, Render render, CancellationToken cancellation)
    {
        LivePack live = RequestPack.Of(http);
        LoadedLegacy? legacy = Guid.TryParse(id, out Guid legacyId) ? await sessions.Read(live, legacyId, cancellation) : null;
        if (legacy is null)
        {
            return await BridgeEndpoints.NotFound(render, http);
        }

        TestShipModel ship = TestShipCapture.Capture(live.Game, live.Pack, legacy.State, legacy.Record.Name);
        TestShipCapture.Saved saved = await TestShipCapture.Save(editor, ship, cancellation);
        var kept = new Dictionary<string, object?> { ["ShipError"] = !saved.Done };
        if (saved.Done)
        {
            kept["ShipMessage"] = $"Saved {ship.Name} as {saved.Id} in ";
            kept["ShipFile"] = saved.File;
            kept["ShipLink"] = TextFile.All.FirstOrDefault(f => f.Path == saved.File) is { } text ? EditorLinks.File(text.Name) : null;
        }
        else
        {
            kept["ShipMessage"] = "Not saved. " + string.Join(" ", saved.Errors.Select(e => e.File.Length == 0 || e.Message.StartsWith(e.File, StringComparison.Ordinal) ? e.Message : $"{e.File}: {e.Message}"));
        }

        return await Patch(render, legacyId, DevReader.Read(legacy.State, live.Game, live.Pack, live.Names, BridgeEndpoints.CycleEvents(game, live, legacy)), kept);
    }

    // Answers with the panel either way: what was set, or why not, with the form as posted.
    private static async Task<IResult> Set(string id, HttpContext http, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation)
    {
        IFormCollection? form = await BridgeEndpoints.ReadForm(http.Request, cancellation);
        string trait = (form is null ? null : BridgeEndpoints.Single(form, "trait"))?.Trim() ?? "";
        string on = (form is null ? null : BridgeEndpoints.Single(form, "on")) ?? DevReader.Ship;
        // A pair's target (1e part three): blank for a trait on one.
        string toward = (form is null ? null : BridgeEndpoints.Single(form, "toward")) ?? "";
        string text = (form is null ? null : BridgeEndpoints.Single(form, "value"))?.Trim() ?? "";
        LivePack live = RequestPack.Of(http);
        if (!Guid.TryParse(id, out Guid legacyId))
        {
            return await BridgeEndpoints.NotFound(render, http);
        }

        var kept = new Dictionary<string, object?> { ["Trait"] = trait, ["On"] = on, ["Toward"] = toward, ["Value"] = text };
        string? crewId = on == DevReader.Ship ? null : on;
        string? targetId = toward.Length == 0 ? null : toward;
        // A trait with levels may be set by a level's name as well as its number.
        TraitDef? def = live.Pack.Traits.FirstOrDefault(t => t.Id == trait);
        if (def?.Levels.FirstOrDefault(l => l.Value == text) is { Value: not null } level)
        {
            text = level.Key.ToString(CultureInfo.InvariantCulture);
        }
        string? done = null;
        string? refused = null;
        DevView? view;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            refused = def is { HasLevels: true } ? $"{trait} is set to 0 or one of its levels: {string.Join(", ", def.Levels.OrderBy(l => l.Key).Select(l => l.Value))}." : $"A trait's value is 0 to {SetTrait.Most}.";
            view = await Current(game, live, legacyId, sessions, cancellation);
        }
        else
        {
            try
            {
                view = await sessions.Run(live, legacyId, legacy =>
                {
                    if (def is { OnPair: true } != (targetId is not null))
                    {
                        throw new CommandRejectedException(def is { OnPair: true } ? $"{trait} sits on a pair: choose whom it points at." : $"{trait} does not sit on a pair, so it points at no one.");
                    }

                    if (targetId is not null && (crewId is null || targetId == crewId))
                    {
                        throw new CommandRejectedException("A pair is two crew members: choose a holder, and another as the target.");
                    }

                    legacy.Send(new SetTrait(trait, crewId, targetId, value));
                    string where = crewId is null ? "the ship" : targetId is null ? live.Names.Crew(crewId) : $"{live.Names.Crew(crewId)} toward {live.Names.Crew(targetId)}";
                    done = value == 0 ? $"Cleared {trait} on {where}." : $"{trait} is now {value} on {where}.";
                    return DevReader.Read(legacy.State, live.Game, live.Pack, live.Names, BridgeEndpoints.CycleEvents(game, live, legacy));
                }, cancellation);
            }
            catch (CommandRejectedException rejected)
            {
                refused = rejected.Message;
                view = await Current(game, live, legacyId, sessions, cancellation);
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

    private static async Task<DevView?> Current(GameServices game, LivePack live, Guid legacyId, LegacySession sessions, CancellationToken cancellation)
    {
        return await sessions.Read(live, legacyId, cancellation) is { } legacy ? DevReader.Read(legacy.State, live.Game, live.Pack, live.Names, BridgeEndpoints.CycleEvents(game, live, legacy)) : null;
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

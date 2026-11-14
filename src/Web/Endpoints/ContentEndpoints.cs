using Sim.Content;
using Sim.Core.Content;
using Web.Components.Content;

namespace Web.Endpoints;

// The editor's routes: mapped only in developer mode. A file path never comes from a request.
public static class ContentEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Every post under /dev is checked once, here.
        RouteGroupBuilder dev = app.MapGroup("/dev").AddEndpointFilter(SameOrigin.Filter);
        dev.MapGet("/content", Index);
        dev.MapPost("/content/reload", Reload);
        dev.MapGet("/content/{kind}", Kind);
        ContentFormEndpoints.Map(dev);
    }

    private static async Task<IResult> Index(ContentHost host, ContentEditor editor, GameServices game, Render render)
    {
        string html = await render.Html<ContentPage>(IndexParameters(host, editor, game, null));
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> Reload(HttpContext http, ContentHost host, ContentEditor editor, GameServices game, Render render, CancellationToken cancellation)
    {
        SaveResult result = await editor.Reload(cancellation);
        string message = result.Saved
            ? "Reloaded from disk. The game now uses the pack on disk."
            : host.Current is null
                ? "The pack on disk is still invalid, so the game has no pack until the files load."
                : "The pack on disk is invalid, so the game keeps the pack it had.";
        Dictionary<string, object?> parameters = IndexParameters(host, editor, game, new IndexAlert(message, result.Saved));
        if (Sse.IsDatastar(http.Request))
        {
            string main = await render.Html<ContentIndex>(parameters);
            return new PatchResult(main);
        }

        string html = await render.Html<ContentPage>(parameters);
        return Results.Content(html, "text/html; charset=utf-8", statusCode: result.Saved ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> Kind(string kind, ContentEditor editor, Render render)
    {
        if (ContentKinds.FromSegment(kind) is not { } known)
        {
            return await NotFound(render);
        }

        ContentFileSet files = editor.Read();
        List<ContentItemRef> items = files.Index().Where(i => i.Kind == known).ToList();
        string html = await render.Html<KindPage>(new Dictionary<string, object?> { ["Kind"] = known, ["Files"] = files, ["Items"] = items });
        return Results.Content(html, "text/html; charset=utf-8");
    }

    // The files are read once per request; the live pack is only for the id and version in play.
    private static Dictionary<string, object?> IndexParameters(ContentHost host, ContentEditor editor, GameServices game, IndexAlert? alert)
    {
        LivePack? live = host.Current;
        ContentFileSet files = editor.Read();
        ContentLoadResult disk = files.Validate();
        IReadOnlyList<ContentItemRef> items = files.Index();
        var problems = new Dictionary<string, IReadOnlyList<ContentLoss>>();
        foreach (string path in files.Files.Keys.OrderBy(p => p, StringComparer.Ordinal))
        {
            // Items are marked in their tables; a file-level problem, or an entry with no id to mark, is listed with its file.
            HashSet<string> ids = items.Where(i => i.File == path).Select(i => i.Id).ToHashSet();
            List<ContentLoss> losses = ContentEdits.Losses(files, path).Where(l => l.Item is null || !ids.Contains(l.Item)).ToList();
            if (losses.Count > 0)
            {
                problems[path] = losses;
            }
        }

        var view = new ContentIndexView(live?.Pack.Id, live?.Pack.Version, disk.Pack?.Id, disk.Pack?.Version, disk.Errors, Directory.Exists(editor.Folder), editor.Folder, game.Store.ListLegacies().Count, files, items, problems);
        return new Dictionary<string, object?> { ["View"] = view, ["Alert"] = alert };
    }

    private static async Task<IResult> NotFound(Render render)
    {
        string html = await render.Html<Web.Components.NotFoundPage>(new Dictionary<string, object?>());
        return Results.Content(html, "text/html; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
    }

    private sealed class PatchResult(string html) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            await Sse.Start(httpContext.Response);
            await Sse.PatchElements(httpContext.Response, html);
        }
    }
}

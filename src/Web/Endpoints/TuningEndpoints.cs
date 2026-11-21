using Sim.Content;
using Web.Components.Tuning;
using Web.Forms;
using Web.Tuning;

namespace Web.Endpoints;

// The Tuning page (1f part one, Task 9; design 4.1 to 4.3), in the developer group beside the editor. A Datastar post is answered with
// patches: a field's change patches the draft's head and the rows whose values or errors moved; Discard and Save patch the whole draft
// form. Any other post gets the whole page. The draft itself is TuningDrafts, one per tuning file in the server's memory.
public static class TuningEndpoints
{
    private const string File = TuningDrafts.Missions;

    public static void Map(RouteGroupBuilder dev)
    {
        dev.MapGet("/tuning", Open);
        dev.MapPost("/tuning/draft", Change);
        dev.MapPost("/tuning/discard", Discard);
        dev.MapPost("/tuning/save", Save);
    }

    private static async Task<IResult> Open(HttpRequest request, TuningDrafts drafts, ContentEditor editor, GameServices game, Render render)
    {
        return await Page(render, DraftView.Build(drafts, editor, game.Clock, File), TuningQuery.From(request.Query), StatusCodes.Status200OK);
    }

    // One field: its values as posted (a number's under its path, a dice field's and a range's under the loader's two names).
    private static async Task<IResult> Change(HttpContext http, TuningDrafts drafts, ContentEditor editor, GameServices game, Render render, CancellationToken cancellation)
    {
        string path = http.Request.Query["path"].ToString();
        FormValues form = await Read(http.Request, cancellation);
        DraftView before = DraftView.Build(drafts, editor, game.Clock, File);
        // A read-only section's numbers are edited as text in part one.
        if (before.Sections.Where(s => !s.ReadOnly).SelectMany(s => Flatten(s.Items)).FirstOrDefault(f => f.Field.Path == path) is not { } field)
        {
            return Results.Text($"{path} is not a field of {File} the form changes.", statusCode: StatusCodes.Status400BadRequest);
        }

        drafts.Set(File, path, field.Names.Select(form.Get).ToList());
        DraftView after = DraftView.Build(drafts, editor, game.Clock, File);
        if (!Sse.IsDatastar(http.Request))
        {
            return await Page(render, after, TuningQuery.Default, StatusCodes.Status200OK);
        }

        // This row, and any other whose error came or went with it (a cap and the number checked against it).
        Dictionary<string, DraftFieldView> was = Fields(before).ToDictionary(f => f.Field.Path);
        var patches = new List<string> { await render.Html<DraftHead>(new Dictionary<string, object?> { ["View"] = after }) };
        foreach (DraftFieldView now in Fields(after).Where(f => f.Field.Path == path || was.GetValueOrDefault(f.Field.Path)?.Error != f.Error))
        {
            patches.Add(await render.Html<DraftField>(new Dictionary<string, object?> { ["Field"] = now }));
        }

        return new PatchesResult(patches);
    }

    private static async Task<IResult> Discard(HttpContext http, TuningDrafts drafts, ContentEditor editor, GameServices game, Render render)
    {
        drafts.Discard(File);
        return await Form(http, render, DraftView.Build(drafts, editor, game.Clock, File), StatusCodes.Status200OK);
    }

    private static async Task<IResult> Save(HttpContext http, TuningDrafts drafts, ContentEditor editor, GameServices game, Render render, CancellationToken cancellation)
    {
        // What the notice says was saved: each changed field and its new value, in file order.
        DraftView before = DraftView.Build(drafts, editor, game.Clock, File);
        List<KeyValuePair<string, string>> changed = Fields(before)
            .Where(f => f.Saved is not null)
            .Select(f => new KeyValuePair<string, string>(f.Field.Path, DraftView.Shown(f.Field.Kind, f.Values)))
            .ToList();
        SaveResult result = await drafts.Save(File, cancellation);
        DraftView view = result.Saved
            ? DraftView.Build(drafts, editor, game.Clock, File, saved: changed)
            : DraftView.Build(drafts, editor, game.Clock, File, refusal: result);
        return await Form(http, render, view, result.Saved ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity);
    }

    private static IEnumerable<DraftFieldView> Fields(DraftView view)
    {
        return view.Sections.SelectMany(s => Flatten(s.Items));
    }

    private static IEnumerable<DraftFieldView> Flatten(IEnumerable<DraftItem> items)
    {
        foreach (DraftItem item in items)
        {
            if (item is DraftFieldItem field)
            {
                yield return field.Field;
            }
            else if (item is DraftGroup group)
            {
                foreach (DraftFieldView inner in Flatten(group.Items))
                {
                    yield return inner;
                }
            }
        }
    }

    private static async Task<IResult> Form(HttpContext http, Render render, DraftView view, int status)
    {
        if (Sse.IsDatastar(http.Request))
        {
            return new PatchesResult([await render.Html<DraftForm>(new Dictionary<string, object?> { ["View"] = view })]);
        }

        return await Page(render, view, TuningQuery.Default, status);
    }

    private static async Task<IResult> Page(Render render, DraftView view, TuningQuery query, int status)
    {
        string html = await render.Html<TuningPage>(new Dictionary<string, object?> { ["Draft"] = view, ["Query"] = query });
        return Results.Content(html, "text/html; charset=utf-8", statusCode: status);
    }

    private static async Task<FormValues> Read(HttpRequest request, CancellationToken cancellation)
    {
        try
        {
            return FormValues.From(await request.ReadFormAsync(cancellation));
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException)
        {
            // No body or no form content type: each value is empty, which the loader refuses beside the field.
            return FormValues.Empty;
        }
    }

    // Each element morphed into the page by its id.
    private sealed class PatchesResult(IReadOnlyList<string> elements) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            await Sse.Start(httpContext.Response);
            foreach (string html in elements)
            {
                await Sse.PatchElements(httpContext.Response, html);
            }
        }
    }
}

using Sim.Content;
using Web.Components.Content;
using Web.Forms;

namespace Web.Endpoints;

// The forms for skills, gear, crew, and assignments. A Datastar post is answered with a patch of the main region; any other
// post (curl, a browser without scripts) with the whole page and the status. Neither moves the address bar: the patched form
// carries its own action, so the next post goes to the item's route.
public static class ContentFormEndpoints
{
    public static void Map(RouteGroupBuilder dev)
    {
        dev.MapGet("/content/{kind}/new", New);
        dev.MapGet("/content/{kind}/{id}", Open);
        dev.MapPost("/content/{kind}", Create);
        dev.MapPost("/content/{kind}/rows", NewRows);
        dev.MapPost("/content/{kind}/{id}", Save);
        dev.MapPost("/content/{kind}/{id}/rows", Rows);
        dev.MapPost("/content/{kind}/{id}/delete", Delete);
    }

    private static async Task<IResult> New(string kind, ContentEditor editor, Render render)
    {
        if (KindForm.For(kind) is not { } form)
        {
            return await NotFound(render, false);
        }

        return await Answer<ItemMain, ItemPage>(render, false, FormParameters(FormFlow.Blank(form, editor.Read())), StatusCodes.Status200OK);
    }

    // After a create the browser comes here with ?saved=1, so a reload shows the item and the notice once the query is gone.
    private static async Task<IResult> Open(string kind, string id, HttpRequest request, ContentEditor editor, Render render)
    {
        string? notice = request.Query["saved"] == "1" ? "Saved." : null;
        if (KindForm.For(kind) is not { } form || FormFlow.Open(form, editor.Read(), id, notice) is not { } view)
        {
            return await NotFound(render, false);
        }

        return await Answer<ItemMain, ItemPage>(render, false, FormParameters(view), StatusCodes.Status200OK);
    }

    private static async Task<IResult> Create(string kind, HttpContext http, ContentEditor editor, Render render, CancellationToken cancellation)
    {
        if (KindForm.For(kind) is not { } form)
        {
            return await NotFound(render, Sse.IsDatastar(http.Request));
        }

        (ItemFormView view, bool saved) = await FormFlow.Create(form, editor, await Read(http.Request, cancellation), cancellation);
        if (saved && Sse.IsDatastar(http.Request))
        {
            return new RedirectResult($"/dev/content/{ContentKinds.Segment(form.Kind)}/{Uri.EscapeDataString(view.Id)}?saved=1");
        }

        return await Answer<ItemMain, ItemPage>(render, Sse.IsDatastar(http.Request), FormParameters(view), saved ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> Save(string kind, string id, HttpContext http, ContentEditor editor, Render render, CancellationToken cancellation)
    {
        bool datastar = Sse.IsDatastar(http.Request);
        if (KindForm.For(kind) is not { } form || await FormFlow.Save(form, editor, id, await Read(http.Request, cancellation), cancellation) is not { } result)
        {
            return await NotFound(render, datastar);
        }

        return await Answer<ItemMain, ItemPage>(render, datastar, FormParameters(result.View), result.Saved ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity);
    }

    private static Task<IResult> NewRows(string kind, HttpContext http, ContentEditor editor, Render render, CancellationToken cancellation)
    {
        return RowsAnswer(kind, null, http, editor, render, cancellation);
    }

    private static Task<IResult> Rows(string kind, string id, HttpContext http, ContentEditor editor, Render render, CancellationToken cancellation)
    {
        return RowsAnswer(kind, id, http, editor, render, cancellation);
    }

    private static async Task<IResult> RowsAnswer(string kind, string? id, HttpContext http, ContentEditor editor, Render render, CancellationToken cancellation)
    {
        bool datastar = Sse.IsDatastar(http.Request);
        if (KindForm.For(kind) is not { } form || FormFlow.Rows(form, editor.Read(), id, await Read(http.Request, cancellation)) is not { } view)
        {
            return await NotFound(render, datastar);
        }

        return await Answer<ItemMain, ItemPage>(render, datastar, FormParameters(view), StatusCodes.Status200OK);
    }

    private static async Task<IResult> Delete(string kind, string id, HttpContext http, ContentEditor editor, Render render, CancellationToken cancellation)
    {
        bool datastar = Sse.IsDatastar(http.Request);
        if (KindForm.For(kind) is not { } form)
        {
            return await NotFound(render, datastar);
        }

        DeleteOutcome outcome = await FormFlow.Delete(form, editor, id, cancellation);
        if (!outcome.Found)
        {
            // Already gone: the same list, with a notice, and no error.
            return await List(render, datastar, editor, form.Kind, $"{id} is not there, so nothing was deleted.");
        }

        if (outcome.Refused is { } refused)
        {
            return await Answer<ItemMain, ItemPage>(render, datastar, FormParameters(refused), StatusCodes.Status422UnprocessableEntity);
        }

        // Deleted: the browser goes to the kind's list, which shows the notice.
        return datastar
            ? new RedirectResult($"/dev/content/{ContentKinds.Segment(form.Kind)}?deleted={Uri.EscapeDataString(id)}")
            : await List(render, datastar, editor, form.Kind, $"Deleted {id}.");
    }

    private static async Task<IResult> List(Render render, bool datastar, ContentEditor editor, ContentKind kind, string notice)
    {
        ContentFileSet files = editor.Read();
        List<ContentItemRef> items = files.Index().Where(i => i.Kind == kind).ToList();
        var parameters = new Dictionary<string, object?> { ["Kind"] = kind, ["Files"] = files, ["Items"] = items, ["Notice"] = notice };
        return await Answer<KindList, KindPage>(render, datastar, parameters, StatusCodes.Status200OK);
    }

    private static Dictionary<string, object?> FormParameters(ItemFormView view)
    {
        return new Dictionary<string, object?> { ["View"] = view };
    }

    private static async Task<FormValues> Read(HttpRequest request, CancellationToken cancellation)
    {
        try
        {
            return FormValues.From(await request.ReadFormAsync(cancellation));
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException)
        {
            // No body or no form content type: every field then fails its own check.
            return FormValues.Empty;
        }
    }

    private static async Task<IResult> Answer<TMain, TPage>(Render render, bool datastar, Dictionary<string, object?> parameters, int status)
        where TMain : Microsoft.AspNetCore.Components.IComponent
        where TPage : Microsoft.AspNetCore.Components.IComponent
    {
        if (datastar)
        {
            return new PatchResult(await render.Html<TMain>(parameters));
        }

        return Results.Content(await render.Html<TPage>(parameters), "text/html; charset=utf-8", statusCode: status);
    }

    private static async Task<IResult> NotFound(Render render, bool datastar)
    {
        if (datastar)
        {
            return new PatchResult(await render.Html<Web.Components.NotFoundMain>(new Dictionary<string, object?>()));
        }

        string html = await render.Html<Web.Components.NotFoundPage>(new Dictionary<string, object?>());
        return Results.Content(html, "text/html; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
    }

    // The documented way to navigate from a stream: a script appended to the body, which removes itself.
    private sealed class RedirectResult(string url) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            await Sse.Start(httpContext.Response);
            string script = $"<script data-effect=\"el.remove()\">window.location = {System.Text.Json.JsonSerializer.Serialize(url)}</script>";
            await Sse.PatchElements(httpContext.Response, script, "body", "append");
        }
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

using Microsoft.AspNetCore.Components;
using Web.Components;

namespace Web;

// The one read of ContentHost.Current a game request makes: taken here, kept for the request, and passed down by handlers.
// With no valid pack (developer mode started on an invalid one) the request is answered here with the page that says so.
internal static class RequestPack
{
    private const string Key = "Web.LivePack";

    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        ContentHost host = http.RequestServices.GetRequiredService<ContentHost>();
        LivePack? live = host.Current;
        if (live is null)
        {
            return await Invalid(http, host);
        }

        http.Items[Key] = live;
        return await next(context);
    }

    public static LivePack Of(HttpContext http)
    {
        return (LivePack)http.Items[Key]!;
    }

    // A Datastar request gets the words as a patch of the main screen, as the other refusals do.
    private static async Task<IResult> Invalid(HttpContext http, ContentHost host)
    {
        Render render = http.RequestServices.GetRequiredService<Render>();
        var parameters = new Dictionary<string, object?> { ["Errors"] = host.LoadErrors };
        if (Sse.IsDatastar(http.Request))
        {
            string main = await render.Html<InvalidPackMain>(parameters);
            return new PatchResult(main);
        }

        string html = await render.Html<InvalidPackPage>(parameters);
        return Results.Content(html, "text/html; charset=utf-8", statusCode: StatusCodes.Status503ServiceUnavailable);
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

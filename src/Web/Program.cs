using GameHost;
using Persistence;
using Sim.Content;
using Sim.Core.Content;
using Web;
using Web.Components;
using Web.Endpoints;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<WebSettings>(builder.Configuration.GetSection("Web"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(provider =>
{
    WebSettings settings = provider.GetRequiredService<IConfiguration>().GetSection("Web").Get<WebSettings>() ?? new WebSettings();
    string contentDirectory = PathResolver.Resolve(settings.ContentDirectory, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
    string databasePath = PathResolver.Resolve(settings.DatabasePath, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
    provider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup").LogInformation("Content directory {ContentDirectory}; database {DatabasePath}.", contentDirectory, databasePath);
    ContentLoadResult content = ContentLoader.LoadDirectory(contentDirectory);
    if (content.Pack is not { } pack)
    {
        throw new InvalidOperationException("The content pack is invalid: " + string.Join("; ", content.Errors.Select(e => $"{e.File}: {e.Message}")));
    }

    return new GameServices(pack, Game.Build(pack), new SqliteLegacyStore($"Data Source={databasePath}"), provider.GetRequiredService<TimeProvider>());
});
builder.Services.AddSingleton<LegacySession>();
builder.Services.AddSingleton<Render>();

WebApplication app = builder.Build();
app.Services.GetRequiredService<GameServices>();

// An unexpected failure: the request may have stored its commands before it failed, so the player is sent to reload.
// A Datastar request gets the words as a patch of the main screen, or the page would not change at all.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        // The client left, for example while another request held the legacy: nothing failed.
    }
    catch (Exception e) when (!context.Response.HasStarted)
    {
        app.Logger.LogError(e, "Request failed.");
        context.Response.Clear();
        if (Sse.IsDatastar(context.Request))
        {
            await Sse.Start(context.Response);
            await Sse.PatchElements(context.Response, ErrorContent.Main);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(ErrorContent.Page);
    }
});
app.UseStaticFiles();

app.MapGet("/", async (GameServices game, Render render) =>
{
    string html = await render.Html<LegaciesPage>(new Dictionary<string, object?> { ["Legacies"] = game.Store.ListLegacies() });
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/legacies", async Task<IResult> (HttpRequest request, GameServices game, LegacySession sessions, Render render, CancellationToken cancellation) =>
{
    async Task<IResult> Refused(string name, string reason)
    {
        string html = await render.Html<LegaciesPage>(new Dictionary<string, object?> { ["Legacies"] = game.Store.ListLegacies(), ["Name"] = name, ["Error"] = reason });
        return Results.Content(html, "text/html; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);
    }

    // Read straight from the form: this is a local single-player app with no cross-site state to protect.
    IFormCollection form;
    try
    {
        form = await request.ReadFormAsync(cancellation);
    }
    catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException)
    {
        // No body, no form content type, or a body over the form limits.
        return await Refused("", "The form was not complete.");
    }

    string name = form["name"].ToString();
    try
    {
        Guid id = await sessions.CreateLegacy(name, cancellation);
        return new SeeOtherResult($"/legacy/{id}");
    }
    catch (InvalidLegacyNameException e)
    {
        return await Refused(name, e.Message);
    }
});

BridgeEndpoints.Map(app);

app.Run();

public partial class Program;

// Written by hand, not rendered: it must not depend on what may have failed.
internal static class ErrorContent
{
    public const string Main = "<main id=\"main\"><div class=\"page-col\"><div class=\"view-head\"><h1>Something went wrong</h1><p>Reload to see where things stand.</p></div><div class=\"actions well\"><a class=\"btn btn--primary\" href=\"/\">Back to the legacies</a></div></div></main>";

    public const string Page = "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Error</title><link rel=\"icon\" href=\"/favicon.svg\" type=\"image/svg+xml\"><link rel=\"stylesheet\" href=\"/css/app.css\"></head><body><div class=\"page\">" + Main + "</div></body></html>";
}

internal sealed class SeeOtherResult(string url) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
        httpContext.Response.Headers.Location = url;
        return Task.CompletedTask;
    }
}

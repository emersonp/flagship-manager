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

// An unexpected failure discards the request's state; the stored legacy is exactly as it was.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception e) when (!context.Response.HasStarted)
    {
        app.Logger.LogError(e, "Request failed.");
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Error</title><link rel=\"icon\" href=\"/favicon.svg\" type=\"image/svg+xml\"><link rel=\"stylesheet\" href=\"/css/app.css\"></head><body><div class=\"page\"><main id=\"main\"><div class=\"page-col\"><div class=\"view-head\"><h1>Something went wrong</h1><p>The request failed, and the legacy was not changed. Go back and try again.</p></div><div class=\"actions well\"><a class=\"btn btn--primary\" href=\"/\">Back to the legacies</a></div></div></main></div></body></html>");
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
    // Read straight from the form: this is a local single-player app with no cross-site state to protect.
    string name = (await request.ReadFormAsync())["name"].ToString();
    try
    {
        Guid id = await sessions.CreateLegacy(name, cancellation);
        return new SeeOtherResult($"/legacy/{id}");
    }
    catch (ArgumentException e)
    {
        string html = await render.Html<LegaciesPage>(new Dictionary<string, object?> { ["Legacies"] = game.Store.ListLegacies(), ["Name"] = name, ["Error"] = e.Message });
        return Results.Content(html, "text/html; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);
    }
});

BridgeEndpoints.Map(app);

app.Run();

public partial class Program;

internal sealed class SeeOtherResult(string url) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
        httpContext.Response.Headers.Location = url;
        return Task.CompletedTask;
    }
}

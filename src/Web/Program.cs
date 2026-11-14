using GameHost;
using Persistence;
using Sim.Content;
using Sim.Core.Content;
using Web;
using Web.Components;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<WebSettings>(builder.Configuration.GetSection("Web"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(provider =>
{
    WebSettings settings = provider.GetRequiredService<IConfiguration>().GetSection("Web").Get<WebSettings>() ?? new WebSettings();
    ContentLoadResult content = ContentLoader.LoadDirectory(settings.ContentDirectory);
    if (content.Pack is not { } pack)
    {
        throw new InvalidOperationException("The content pack is invalid: " + string.Join("; ", content.Errors.Select(e => $"{e.File}: {e.Message}")));
    }

    return new GameServices(pack, Game.Build(pack), new SqliteLegacyStore($"Data Source={settings.DatabasePath}"), provider.GetRequiredService<TimeProvider>());
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
        await context.Response.WriteAsync("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Error</title></head><body><main id=\"main\"><h1>Something went wrong</h1><p>The legacy was not changed.</p><p><a href=\"/\">Back to the legacies</a></p></main></body></html>");
    }
});
app.UseStaticFiles();

const int MaxNameLength = 60;

app.MapGet("/", async (GameServices game, Render render) =>
{
    string html = await render.Html<LegaciesPage>(new Dictionary<string, object?> { ["Legacies"] = game.Store.ListLegacies() });
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/legacies", async Task<IResult> (HttpRequest request, GameServices game, LegacySession sessions, Render render) =>
{
    // Read straight from the form: this is a local single-player app with no cross-site state to protect.
    string name = (await request.ReadFormAsync())["name"].ToString().Trim();
    string? error = name.Length == 0 ? "Enter a name." : name.Length > MaxNameLength ? $"Use {MaxNameLength} characters or fewer." : null;
    if (error is not null)
    {
        string html = await render.Html<LegaciesPage>(new Dictionary<string, object?> { ["Legacies"] = game.Store.ListLegacies(), ["Name"] = name, ["Error"] = error });
        return Results.Content(html, "text/html; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);
    }

    Guid id = await sessions.CreateLegacy(name);
    return new SeeOtherResult($"/legacy/{id}");
});

app.MapGet("/legacy/{id}", async (string id, LegacySession sessions, Render render) =>
{
    LoadedLegacy? legacy = Guid.TryParse(id, out Guid legacyId) ? await sessions.Read(legacyId) : null;
    if (legacy is null)
    {
        string notFound = await render.Html<NotFoundPage>(new Dictionary<string, object?>());
        return Results.Content(notFound, "text/html; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
    }

    string html = await render.Html<BridgePage>(new Dictionary<string, object?> { ["Record"] = legacy.Record });
    return Results.Content(html, "text/html; charset=utf-8");
});

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

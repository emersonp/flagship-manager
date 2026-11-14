using System.Text;
using Microsoft.AspNetCore.Http;
using Persistence;
using Sim.Core;
using Web;

namespace Web.Tests;

public sealed class SseTests : IDisposable
{
    private readonly TestApp app = new();

    public void Dispose()
    {
        app.Dispose();
    }

    private static async Task<string> Written(Func<HttpResponse, Task> write)
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        await write(context.Response);
        return Encoding.UTF8.GetString(body.ToArray());
    }

    [Fact]
    public async Task PatchElements_writes_one_data_line_per_html_line_with_a_blank_line_after()
    {
        string wire = await Written(r => Sse.PatchElements(r, "<div id=\"hal\">\r\n    Waiting.\n</div>"));

        Assert.Equal("event: datastar-patch-elements\ndata: elements <div id=\"hal\">\ndata: elements     Waiting.\ndata: elements </div>\n\n", wire);
    }

    [Fact]
    public async Task PatchElements_writes_selector_and_mode_before_the_elements()
    {
        string wire = await Written(r => Sse.PatchElements(r, "<li id=\"b1\">x</li>", "#beats", "append"));

        Assert.Equal("event: datastar-patch-elements\ndata: selector #beats\ndata: mode append\ndata: elements <li id=\"b1\">x</li>\n\n", wire);
    }

    [Fact]
    public async Task PatchSignals_writes_the_json_on_one_data_line()
    {
        string wire = await Written(r => Sse.PatchSignals(r, "{\"_open\":true}"));

        Assert.Equal("event: datastar-patch-signals\ndata: signals {\"_open\":true}\n\n", wire);
    }

    [Fact]
    public async Task Redirect_appends_a_script_to_the_body()
    {
        string wire = await Written(r => Sse.Redirect(r, "/legacy/x"));

        Assert.Equal("event: datastar-patch-elements\ndata: selector body\ndata: mode append\ndata: elements <script>window.location = \"/legacy/x\"</script>\n\n", wire);
    }

    [Fact]
    public async Task Start_sets_the_event_stream_headers()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await Sse.Start(context.Response);

        Assert.Equal("text/event-stream", context.Response.ContentType);
        Assert.Equal("no-cache", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Two_concurrent_runs_on_one_legacy_run_one_after_the_other()
    {
        Guid id = await app.Sessions.CreateLegacy("LEGACY");
        long before = (await app.Sessions.Read(id))!.State.NextSequence;

        Task<object?>[] runs = Enumerable.Range(0, 2).Select(_ => app.Sessions.Run<object>(id, legacy =>
        {
            legacy.Send(new AdvancePhase());
            return legacy.State.Phase;
        })).ToArray();
        await Task.WhenAll(runs);

        LoadedLegacy stored = (await app.Sessions.Read(id))!;
        IReadOnlyList<EventEnvelope> events = app.Game.Store.ReadEvents(id);
        Assert.Equal(CyclePhase.Resolution, stored.State.Phase);
        Assert.Equal(events.Count + 1, stored.State.NextSequence);
        Assert.Equal(events.Select(e => e.Sequence).Order(), events.Select(e => e.Sequence).Distinct().Order());
        Assert.True(stored.State.NextSequence > before);
    }

    [Fact]
    public async Task A_rejected_command_leaves_the_stored_legacy_unchanged()
    {
        Guid id = await app.Sessions.CreateLegacy("LEGACY");
        LoadedLegacy before = (await app.Sessions.Read(id))!;
        int eventsBefore = app.Game.Store.ReadEvents(id).Count;

        await Assert.ThrowsAsync<CommandRejectedException>(() => app.Sessions.Run<object>(id, legacy =>
        {
            legacy.Send(new AdvancePhase());
            legacy.Send(new FoundLegacy());
            return new object();
        }));

        LoadedLegacy after = (await app.Sessions.Read(id))!;
        Assert.Equal(before.State.NextSequence, after.State.NextSequence);
        Assert.Equal(before.State.Phase, after.State.Phase);
        Assert.Equal(eventsBefore, app.Game.Store.ReadEvents(id).Count);
    }

    [Fact]
    public async Task Run_on_an_unknown_legacy_returns_null()
    {
        Assert.Null(await app.Sessions.Run<object>(Guid.NewGuid(), _ => new object()));
    }
}

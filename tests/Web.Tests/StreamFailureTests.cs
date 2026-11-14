using System.Net;
using Microsoft.Extensions.Logging;

namespace Web.Tests;

// A Datastar request that fails still shows the player what happened; a plain request keeps its page and status.
public sealed class StreamFailureTests : IDisposable
{
    private readonly TestApp app = new();
    private readonly HttpClient client;

    public StreamFailureTests()
    {
        client = app.NewClient();
    }

    public void Dispose()
    {
        app.Dispose();
    }

    private static HttpRequestMessage Request(string url, bool datastar, IEnumerable<KeyValuePair<string, string>> form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        if (datastar)
        {
            request.Headers.Add("Datastar-Request", "true");
        }

        return request;
    }

    private static string MainPatch(string stream)
    {
        Assert.StartsWith("event: datastar-patch-elements\ndata: elements <main id=\"main\"", stream);
        return stream;
    }

    [Theory]
    [InlineData("advance")]
    [InlineData("launch")]
    [InlineData("answer")]
    public async Task A_datastar_post_for_an_unknown_legacy_patches_main_with_not_found(string route)
    {
        foreach (string id in new[] { Guid.NewGuid().ToString(), "not-a-guid" })
        {
            HttpResponseMessage response = await client.SendAsync(Request($"/legacy/{id}/{route}", true, [new("cycle", "1"), new("phase", "Orders")]));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            string stream = MainPatch(await response.Content.ReadAsStringAsync());
            Assert.Contains("<h1>Not found</h1>", stream);
            Assert.DoesNotContain("<html", stream);
        }
    }

    [Fact]
    public async Task A_plain_post_for_an_unknown_legacy_keeps_the_404_page()
    {
        HttpResponseMessage response = await client.SendAsync(Request($"/legacy/{Guid.NewGuid()}/advance", false, []));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("<html", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_failure_after_the_commit_shows_the_error_content_without_claiming_nothing_changed(bool datastar)
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        List<KeyValuePair<string, string>> stamp = app.Stamp(id);
        app.Sessions.AfterCommit = _ => throw new InvalidOperationException("Test failure after the commit.");

        HttpResponseMessage response = await client.SendAsync(Request($"/legacy/{id}/advance", datastar, stamp));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(datastar ? HttpStatusCode.OK : HttpStatusCode.InternalServerError, response.StatusCode);
        if (datastar)
        {
            MainPatch(body);
            Assert.DoesNotContain("<html", body);
        }
        else
        {
            Assert.Contains("<html", body);
        }

        Assert.Contains("Something went wrong", body);
        Assert.Contains("Reload to see where things stand.", body);
        Assert.DoesNotContain("not changed", body);
        Assert.Contains(app.Logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Request failed."));
    }

    // A client that gives up while another request holds the legacy is not a failure.
    [Fact]
    public async Task A_request_cancelled_while_waiting_for_the_lock_is_not_logged_as_a_failure()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        List<KeyValuePair<string, string>> stamp = app.Stamp(id);
        var held = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        Task holder = Task.Run(() => app.Sessions.Run<object>(app.Live, id, _ =>
        {
            held.SetResult();
            release.Task.Wait();
            return new object();
        }));
        await held.Task;
        var finished = new TaskCompletionSource();
        app.Logs.OnEntry = e =>
        {
            if (e.Message.Contains("Request finished") && e.Message.Contains("/advance"))
            {
                finished.TrySetResult();
            }
        };

        var waitingForLock = new TaskCompletionSource();
        app.Sessions.BeforeLock = _ => waitingForLock.TrySetResult();
        using var cancel = new CancellationTokenSource();
        Task<HttpResponseMessage> waiting = client.SendAsync(Request($"/legacy/{id}/advance", true, stamp), cancel.Token);
        await waitingForLock.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
        release.SetResult();
        await holder;

        Assert.DoesNotContain(app.Logs.Entries, e => e.Level >= LogLevel.Error);
        Assert.DoesNotContain(app.Logs.Entries, e => e.Message.Contains("Request finished") && e.Message.Contains(" - 500 "));
    }
}

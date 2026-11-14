using System.Text;
using Microsoft.AspNetCore.Http;
using Web;

namespace Web.Tests;

public sealed class SseTests
{
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
    public async Task Start_sets_the_event_stream_headers()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await Sse.Start(context.Response);

        Assert.Equal("text/event-stream", context.Response.ContentType);
        Assert.Equal("no-cache", context.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("#a\n#b", "append")]
    [InlineData("#a", "append\r\nevent: x")]
    public async Task A_selector_or_mode_with_a_line_break_is_refused(string selector, string mode)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Written(r => Sse.PatchElements(r, "<p id=\"a\"></p>", selector, mode)));
    }
}

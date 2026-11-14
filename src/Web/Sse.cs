using System.Text;
using Microsoft.AspNetCore.Http;

namespace Web;

// Datastar 1.0.5 stream events, written by hand.
public static class Sse
{
    public static Task Start(HttpResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        return response.StartAsync();
    }

    public static Task PatchElements(HttpResponse response, string html, string? selector = null, string? mode = null)
    {
        var text = new StringBuilder("event: datastar-patch-elements\n");
        if (selector is not null)
        {
            text.Append("data: selector ").Append(selector).Append('\n');
        }

        if (mode is not null)
        {
            text.Append("data: mode ").Append(mode).Append('\n');
        }

        foreach (string line in html.ReplaceLineEndings("\n").Split('\n'))
        {
            text.Append("data: elements ").Append(line).Append('\n');
        }

        return Write(response, text.Append('\n'));
    }

    public static Task PatchSignals(HttpResponse response, string json)
    {
        return Write(response, new StringBuilder("event: datastar-patch-signals\ndata: signals ").Append(json).Append("\n\n"));
    }

    public static Task Redirect(HttpResponse response, string url)
    {
        string script = $"<script>window.location = \"{url.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"</script>";
        return PatchElements(response, script, "body", "append");
    }

    private static async Task Write(HttpResponse response, StringBuilder text)
    {
        await response.WriteAsync(text.ToString());
        await response.Body.FlushAsync();
    }
}

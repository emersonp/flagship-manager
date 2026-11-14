using System.Text;
using Microsoft.AspNetCore.Http;

namespace Web;

// Datastar 1.0.5 stream events, written by hand.
public static class Sse
{
    // Datastar sends this header with every request it makes.
    public static bool IsDatastar(HttpRequest request)
    {
        return request.Headers["Datastar-Request"] == "true";
    }

    public static Task Start(HttpResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        return response.StartAsync();
    }

    public static Task PatchElements(HttpResponse response, string html, string? selector = null, string? mode = null)
    {
        RefuseLineBreak(selector, nameof(selector));
        RefuseLineBreak(mode, nameof(mode));
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

    private static void RefuseLineBreak(string? value, string name)
    {
        if (value is not null && value.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new ArgumentException("A line break would end the stream field.", name);
        }
    }

    private static async Task Write(HttpResponse response, StringBuilder text)
    {
        await response.WriteAsync(text.ToString());
        await response.Body.FlushAsync();
    }
}

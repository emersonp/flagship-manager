namespace Web.Endpoints;

// The editor's posts write files on the author's machine, so they must come from the app's own pages: a page open in the
// same browser must not be able to post to localhost. A request with neither header (curl, the tests' client) is allowed.
// Every request must also name a loopback host, so a foreign name rebound to this machine reaches nothing (DNS rebinding).
internal static class SameOrigin
{
    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpRequest request = context.HttpContext.Request;
        if (!IsLoopbackName(request.Host.Host))
        {
            return Results.Text("The content editor answers only on localhost, 127.0.0.1, or [::1].", statusCode: StatusCodes.Status403Forbidden);
        }

        if (HttpMethods.IsPost(request.Method) && !Allowed(request))
        {
            return Results.Text("A post to the content editor must come from the editor's own pages.", statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }

    private static bool IsLoopbackName(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "[::1]";
    }

    private static bool Allowed(HttpRequest request)
    {
        if (request.Headers.TryGetValue("Sec-Fetch-Site", out Microsoft.Extensions.Primitives.StringValues site) && (site.Count != 1 || site[0] != "same-origin"))
        {
            return false;
        }

        if (!request.Headers.TryGetValue("Origin", out Microsoft.Extensions.Primitives.StringValues origin))
        {
            return true;
        }

        // The opaque origin "null" is not a URL, so it is refused with every other origin that is not this one.
        if (origin.Count != 1 || !Uri.TryCreate(origin[0], UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        int port = request.Host.Port ?? (request.Scheme == Uri.UriSchemeHttps ? 443 : 80);
        return uri.Scheme == request.Scheme && string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase) && uri.Port == port;
    }
}

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Web;

// Renders a component to a string, outside any request pipeline.
public sealed class Render(IServiceProvider services, ILoggerFactory loggers)
{
    public async Task<string> Html<TComponent>(IDictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(services, loggers);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return root.ToHtmlString();
        });
    }
}

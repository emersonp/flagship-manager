using System.Net;
using Sim.Core;
using Sim.Missions;
using SimRunner;

namespace Web.Tests;

// Plays a legacy through the web requests: reads it as the web layer does, asks the automatic captain, posts the equivalent request.
internal sealed class WebCaptain(TestApp app, HttpClient client, Guid id, Func<LegacyState, ICommand?>? choose = null)
{
    // What the captain would send now, after any test override.
    public async Task<ICommand> Peek()
    {
        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;
        return choose?.Invoke(legacy.State) ?? AutoCaptain.NextCommand(app.Live.Game, legacy.State, app.Live.Pack);
    }

    // Returns the route posted and the stream it answered with.
    public async Task<(string Route, string Stream)> Step()
    {
        ICommand command = await Peek();
        switch (command)
        {
            case AssignTeam team:
                return ("launch", await Post("launch", Form(team)));
            case AnswerCall answer:
                return ("answer", await Post("answer", [new("mission", answer.MissionId), new("call", answer.CallId), new("answer", answer.Answer.ToString())]));
            case AdvancePhase:
                return ("advance", await Post("advance", []));
            default:
                throw new InvalidOperationException($"The captain asked for {command}.");
        }
    }

    public async Task<LoadedLegacy> Read()
    {
        return (await app.Sessions.Read(app.Live, id))!;
    }

    // Posts with the cycle and phase the page shows now, as the browser does, unless the form carries its own.
    public async Task<string> Post(string route, IEnumerable<KeyValuePair<string, string>> form)
    {
        HttpResponseMessage response = await client.PostAsync($"/legacy/{id}/{route}", new FormUrlEncodedContent(app.Stamped(id, form)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static List<KeyValuePair<string, string>> Form(AssignTeam team)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("objective", team.ObjectiveId),
            new("lead", team.LeadId),
            new("priority", team.Parameters.Priority.ToString()),
            new("autonomy", team.Parameters.Autonomy.ToString()),
        };
        form.AddRange(team.CrewIds.Select(c => new KeyValuePair<string, string>("crew", c)));
        form.AddRange(team.GearIds.Select(g => new KeyValuePair<string, string>("gear", g)));
        return form;
    }
}

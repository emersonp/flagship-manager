using System.Net;
using Sim.Core;
using Sim.Missions;
using SimRunner;

namespace Web.Tests;

// Plays a legacy through the web requests: reads it as the web layer does, asks the automatic captain, posts the equivalent request.
internal sealed class WebCaptain(TestApp app, HttpClient client, Guid id)
{
    // Returns the route posted and the stream it answered with.
    public async Task<(string Route, string Stream)> Step()
    {
        LoadedLegacy legacy = (await app.Sessions.Read(id))!;
        ICommand command = AutoCaptain.NextCommand(app.Game.Game, legacy.State, app.Game.Pack);
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
        return (await app.Sessions.Read(id))!;
    }

    public async Task<string> Post(string route, IEnumerable<KeyValuePair<string, string>> form)
    {
        HttpResponseMessage response = await client.PostAsync($"/legacy/{id}/{route}", new FormUrlEncodedContent(form));
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
            new("risk", team.Parameters.Risk.ToString()),
            new("autonomy", team.Parameters.Autonomy.ToString()),
        };
        form.AddRange(team.CrewIds.Select(c => new KeyValuePair<string, string>("crew", c)));
        form.AddRange(team.GearIds.Select(g => new KeyValuePair<string, string>("gear", g)));
        return form;
    }
}

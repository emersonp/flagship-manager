using System.Threading.Channels;
using MissionBench;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;
using Web.Bridge;
using Web.Components.Tuning;
using Web.Forms;
using Web.Tuning;

namespace Web.Endpoints;

// The Tuning page (1f part one, Task 9; design 4.1 to 4.3), in the developer group beside the editor. A Datastar post is answered with
// patches: a field's change patches the draft's head and the rows whose values or errors moved; Discard and Save patch the whole draft
// form. Any other post gets the whole page. The draft itself is TuningDrafts, one per tuning file in the server's memory. The bench
// (Task 10; design 4.4 to 4.8): a change of input redraws the inputs and cancels the page's run; Run streams the figures as they are
// computed (TuningRuns), saved and draft on the same seeds; in Every objective (Task 11), a row of every objective as each finishes.
public static class TuningEndpoints
{
    private const string File = TuningDrafts.Missions;

    public static void Map(RouteGroupBuilder dev)
    {
        dev.MapGet("/tuning", Open);
        dev.MapPost("/tuning/draft", Change);
        dev.MapPost("/tuning/discard", Discard);
        dev.MapPost("/tuning/save", Save);
        dev.MapPost("/tuning/inputs", Inputs);
        dev.MapPost("/tuning/run", Run);
    }

    private static async Task<IResult> Open(HttpContext http, TuningDrafts drafts, ContentEditor editor, GameServices game, Render render)
    {
        return await Page(http, render, DraftView.Build(drafts, editor, game.Clock, File), TuningQuery.From(http.Request.Query), StatusCodes.Status200OK);
    }

    // One field: its values as posted (a number's under its path, a dice field's and a range's under the loader's two names).
    private static async Task<IResult> Change(HttpContext http, TuningDrafts drafts, ContentEditor editor, GameServices game, Render render, CancellationToken cancellation)
    {
        string path = http.Request.Query["path"].ToString();
        FormValues form = await Read(http.Request, cancellation);
        DraftView before = DraftView.Build(drafts, editor, game.Clock, File);
        // A read-only section's numbers are edited as text in part one.
        if (before.Sections.Where(s => !s.ReadOnly).SelectMany(s => Flatten(s.Items)).FirstOrDefault(f => f.Field.Path == path) is not { } field)
        {
            return Results.Text($"{path} is not a field of {File} the form changes.", statusCode: StatusCodes.Status400BadRequest);
        }

        drafts.Set(File, path, field.Names.Select(form.Get).ToList());
        DraftView after = DraftView.Build(drafts, editor, game.Clock, File);
        if (!Sse.IsDatastar(http.Request))
        {
            return await Page(http, render, after, TuningQuery.Default, StatusCodes.Status200OK);
        }

        // This row, and any other whose error came or went with it (a cap and the number checked against it); and Run, which a draft
        // that does not load refuses.
        Dictionary<string, DraftFieldView> was = Fields(before).ToDictionary(f => f.Field.Path);
        var patches = new List<string> { await render.Html<DraftHead>(new Dictionary<string, object?> { ["View"] = after }), await RunActsHtml(render, drafts) };
        foreach (DraftFieldView now in Fields(after).Where(f => f.Field.Path == path || was.GetValueOrDefault(f.Field.Path)?.Error != f.Error))
        {
            patches.Add(await render.Html<DraftField>(new Dictionary<string, object?> { ["Field"] = now }));
        }

        return new PatchesResult(patches);
    }

    private static async Task<IResult> Discard(HttpContext http, TuningDrafts drafts, ContentEditor editor, GameServices game, Render render)
    {
        drafts.Discard(File);
        return await Form(http, render, drafts, DraftView.Build(drafts, editor, game.Clock, File), StatusCodes.Status200OK);
    }

    private static async Task<IResult> Save(HttpContext http, TuningDrafts drafts, ContentEditor editor, GameServices game, Render render, CancellationToken cancellation)
    {
        // What the notice says was saved: each changed field and its new value, in file order.
        DraftView before = DraftView.Build(drafts, editor, game.Clock, File);
        List<KeyValuePair<string, string>> changed = Fields(before)
            .Where(f => f.Saved is not null)
            .Select(f => new KeyValuePair<string, string>(f.Field.Path, DraftView.Shown(f.Field.Kind, f.Values)))
            .ToList();
        SaveResult result = await drafts.Save(File, cancellation);
        DraftView view = result.Saved
            ? DraftView.Build(drafts, editor, game.Clock, File, saved: changed)
            : DraftView.Build(drafts, editor, game.Clock, File, refusal: result);
        return await Form(http, render, drafts, view, result.Saved ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity);
    }

    // A change of input: the page's run is cancelled, and the inputs are drawn again from what was posted (a real team's source, its
    // roster, and the lead from the picked), with the address and the view links following them.
    private static async Task<IResult> Inputs(HttpContext http, TuningRuns runs, Render render)
    {
        IFormCollection form = await ReadForm(http.Request, http.RequestAborted);
        TuningQuery query = TuningQuery.From(form);
        string page = PageOf(http.Request, form);
        runs.Cancel(page, RunStop.Inputs);
        if (!Sse.IsDatastar(http.Request))
        {
            return Results.Redirect(query.Href());
        }

        BenchInputsView inputs = await InputsView(http, query, page, null, null);
        return new PatchesResult(
        [
            await render.Html<BenchInputs>(new Dictionary<string, object?> { ["View"] = inputs }),
            await render.Html<BenchViews>(new Dictionary<string, object?> { ["Query"] = query }),
        ]);
    }

    // Run: the inputs as posted, judged in the command line's words; a real team built once by the launch's own code; and the draft,
    // when there is one and "Run saved only" was not asked for. A refusal redraws the inputs with it. Otherwise the figures stream as the
    // run goes (Datastar), or the page comes back with them once it is done (no script).
    private static async Task<IResult> Run(HttpContext http, TuningRuns runs, TuningDrafts drafts, ContentEditor editor, GameServices game, Render render)
    {
        CancellationToken aborted = http.RequestAborted;
        IFormCollection form = await ReadForm(http.Request, aborted);
        TuningQuery query = TuningQuery.From(form);
        string page = PageOf(http.Request, form);
        bool savedOnly = http.Request.Query["saved"] == "only";
        runs.Cancel(page, RunStop.NewRun);
        RunPrepared prepared = await runs.Prepare(query, savedOnly, aborted);
        if (prepared.Plan is not { } plan || prepared.Names is not { } names)
        {
            BenchInputsView inputs = await InputsView(http, query, page, prepared.Errors, prepared.Refused);
            if (Sse.IsDatastar(http.Request))
            {
                return new PatchesResult([await render.Html<BenchInputs>(new Dictionary<string, object?> { ["View"] = inputs })]);
            }

            return await Page(http, render, DraftView.Build(drafts, editor, game.Clock, File), query, StatusCodes.Status422UnprocessableEntity, inputs);
        }

        RunTicket ticket = runs.Begin(page, aborted);
        if (query.View == "every")
        {
            return await RunEvery(http, plan, ticket, runs, drafts, editor, game, render, names, query, page);
        }

        if (Sse.IsDatastar(http.Request))
        {
            return new RunStream(plan, ticket, runs, render, names, game.Clock);
        }

        RunSnapshot snapshot;
        try
        {
            snapshot = TuningRuns.Run(plan, null, game.Clock, TimeSpan.Zero, ticket.Token);
        }
        finally
        {
            runs.End(ticket);
        }

        BenchInputsView shown = await InputsView(http, query, page, null, null);
        return await Page(http, render, DraftView.Build(drafts, editor, game.Clock, File), query, StatusCodes.Status200OK, shown, snapshot, snapshot.Finished ? null : ticket.Stop ?? RunStop.Left);
    }

    // Every objective (Task 11; design 4.8): each objective in turn, saved and draft on the same seeds, streamed a row as it finishes;
    // or, without script, the page once every row is done.
    private static async Task<IResult> RunEvery(
        HttpContext http,
        RunPlan plan,
        RunTicket ticket,
        TuningRuns runs,
        TuningDrafts drafts,
        ContentEditor editor,
        GameServices game,
        Render render,
        Names names,
        TuningQuery query,
        string page)
    {
        if (Sse.IsDatastar(http.Request))
        {
            return new EveryStream(plan, ticket, runs, render, names, query);
        }

        EverySnapshot snapshot;
        try
        {
            snapshot = TuningRuns.RunEvery(plan, null, ticket.Token);
        }
        finally
        {
            runs.End(ticket);
        }

        BenchInputsView shown = await InputsView(http, query, page, null, null);
        return await Page(http, render, DraftView.Build(drafts, editor, game.Clock, File), query, StatusCodes.Status200OK, shown, every: snapshot, stop: snapshot.Finished ? null : ticket.Stop ?? RunStop.Left);
    }

    private static IEnumerable<DraftFieldView> Fields(DraftView view)
    {
        return view.Sections.SelectMany(s => Flatten(s.Items));
    }

    private static IEnumerable<DraftFieldView> Flatten(IEnumerable<DraftItem> items)
    {
        foreach (DraftItem item in items)
        {
            if (item is DraftFieldItem field)
            {
                yield return field.Field;
            }
            else if (item is DraftGroup group)
            {
                foreach (DraftFieldView inner in Flatten(group.Items))
                {
                    yield return inner;
                }
            }
        }
    }

    private static async Task<IResult> Form(HttpContext http, Render render, TuningDrafts drafts, DraftView view, int status)
    {
        if (Sse.IsDatastar(http.Request))
        {
            return new PatchesResult([await render.Html<DraftForm>(new Dictionary<string, object?> { ["View"] = view }), await RunActsHtml(render, drafts)]);
        }

        return await Page(http, render, view, TuningQuery.Default, status);
    }

    // Run and "Run saved only" as the draft leaves them: Run refused while a draft does not load.
    private static Task<string> RunActsHtml(Render render, TuningDrafts drafts)
    {
        bool broken = drafts.Current(File) is not null && drafts.Load(File).Tuning is null;
        return render.Html<RunActs>(new Dictionary<string, object?> { ["DraftBroken"] = broken });
    }

    // The whole page: the draft, and the bench with its inputs (and a run's figures, for a run posted without script).
    private static async Task<IResult> Page(
        HttpContext http,
        Render render,
        DraftView view,
        TuningQuery query,
        int status,
        BenchInputsView? inputs = null,
        RunSnapshot? snapshot = null,
        RunStop? stop = null,
        EverySnapshot? every = null)
    {
        LivePack? live = http.RequestServices.GetRequiredService<ContentHost>().Current;
        inputs ??= await InputsView(http, query, NewPage(), null, null);
        string html = await render.Html<TuningPage>(new Dictionary<string, object?>
        {
            ["Draft"] = view,
            ["Query"] = query,
            ["Inputs"] = inputs,
            ["Names"] = live?.Names,
            ["Snapshot"] = snapshot,
            ["Every"] = every,
            ["Stop"] = stop,
        });
        return Results.Content(html, "text/html; charset=utf-8", statusCode: status);
    }

    private static Task<BenchInputsView> InputsView(HttpContext http, TuningQuery query, string page, IReadOnlyDictionary<string, string>? errors, string? refused)
    {
        IServiceProvider services = http.RequestServices;
        return BenchInputsView.Build(
            query,
            page,
            services.GetRequiredService<ContentHost>().Current,
            services.GetRequiredService<BenchTeams>(),
            services.GetRequiredService<GameServices>(),
            services.GetRequiredService<TuningDrafts>(),
            errors,
            refused,
            http.RequestAborted);
    }

    // The page's id as a post names it (the _page signal, or the form's own for a post without script), or a new one.
    private static string PageOf(HttpRequest request, IFormCollection form)
    {
        string page = request.Query["page"].ToString();
        page = page.Length > 0 ? page : form["page"].ToString();
        return page.Length is > 0 and <= 64 && page.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? page : NewPage();
    }

    private static string NewPage()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static async Task<FormValues> Read(HttpRequest request, CancellationToken cancellation)
    {
        try
        {
            return FormValues.From(await request.ReadFormAsync(cancellation));
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException)
        {
            // No body or no form content type: each value is empty, which the loader refuses beside the field.
            return FormValues.Empty;
        }
    }

    private static async Task<IFormCollection> ReadForm(HttpRequest request, CancellationToken cancellation)
    {
        try
        {
            return await request.ReadFormAsync(cancellation);
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException)
        {
            // No body or no form content type: every input at its default.
            return FormCollection.Empty;
        }
    }

    // A run's figures streamed as it goes (design 4.7). The run is on a background task, and each snapshot it gives is put in a channel
    // that holds one, dropping the older, so a slow client costs the bench at most one pending patch. The first figures are patched
    // whole; after that the progress line, the summary, and each encounter's summary and table are patched on their own (so a fold the
    // author opened stays open), and the list of encounters whole only when one is first reached. A run stopped by a change of input or
    // of the draft says so; one stopped by a new run on the page, or by the page leaving, writes nothing more.
    private sealed class RunStream(RunPlan plan, RunTicket ticket, TuningRuns runs, Render render, Names names, TimeProvider clock) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            HttpResponse response = httpContext.Response;
            try
            {
                await Sse.Start(response);
                await Sse.PatchElements(response, await Whole(TuningRuns.Start(plan), null));
                Channel<RunSnapshot> channel = Channel.CreateBounded<RunSnapshot>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
                Task<RunSnapshot> work = Task.Run(() =>
                {
                    try
                    {
                        return TuningRuns.Run(plan, s => channel.Writer.TryWrite(s), clock, TuningRuns.PatchInterval, ticket.Token);
                    }
                    finally
                    {
                        channel.Writer.TryComplete();
                    }
                });
                IReadOnlyList<string>? encounters = null;
                await foreach (RunSnapshot snapshot in channel.Reader.ReadAllAsync())
                {
                    if (!ticket.Token.IsCancellationRequested)
                    {
                        encounters = await Write(response, snapshot, null, encounters);
                    }
                }

                RunSnapshot last = await work;
                if (ticket.Stop is RunStop.NewRun || httpContext.RequestAborted.IsCancellationRequested)
                {
                    return;
                }

                await Write(response, last, last.Finished ? null : ticket.Stop ?? RunStop.Left, encounters);
            }
            finally
            {
                runs.End(ticket);
            }
        }

        private async Task<IReadOnlyList<string>?> Write(HttpResponse response, RunSnapshot snapshot, RunStop? stop, IReadOnlyList<string>? sent)
        {
            if (snapshot.Done == 0)
            {
                await Sse.PatchElements(response, await Whole(snapshot, stop));
                return null;
            }

            IReadOnlyList<string> now = FigureRows.Encounters(RunFigures.AllColumns(snapshot));
            if (sent is null)
            {
                await Sse.PatchElements(response, await Whole(snapshot, stop));
                return now;
            }

            await Sse.PatchElements(response, await render.Html<RunProgress>(new Dictionary<string, object?>
            {
                ["Done"] = snapshot.Done,
                ["Runs"] = snapshot.Runs,
                ["Seed"] = snapshot.Seed,
                ["Columns"] = RunFigures.ColumnCount(snapshot),
                ["Stop"] = stop,
            }));
            await Sse.PatchElements(response, await render.Html<FigureTable>(new Dictionary<string, object?>
            {
                ["Id"] = RunFigures.SummaryId,
                ["Caption"] = RunFigures.Caption("Summary", snapshot),
                ["Snapshot"] = snapshot,
                ["Rows"] = FigureRows.Summary(RunFigures.AllColumns(snapshot)),
                ["Foot"] = RunFigures.WorstRow(snapshot),
            }));
            if (!now.SequenceEqual(sent))
            {
                await Sse.PatchElements(response, await render.Html<EncounterList>(new Dictionary<string, object?> { ["Snapshot"] = snapshot, ["Names"] = names }));
                return now;
            }

            foreach (string id in now)
            {
                foreach (string part in new[] { "summary", "table" })
                {
                    await Sse.PatchElements(response, await render.Html<EncounterTable>(new Dictionary<string, object?>
                    {
                        ["EncounterId"] = id,
                        ["Name"] = names.Encounter(id),
                        ["Snapshot"] = snapshot,
                        ["Part"] = part,
                    }));
                }
            }

            return now;
        }

        private Task<string> Whole(RunSnapshot snapshot, RunStop? stop)
        {
            return render.Html<RunFigures>(new Dictionary<string, object?> { ["Snapshot"] = snapshot, ["Stop"] = stop, ["Names"] = names });
        }
    }

    // Every objective streamed as it goes (Task 11): the view whole at the start, with every objective's row waiting; then, as each row
    // finishes, the progress line and that row alone, through the same channel of one as RunStream (a snapshot holds every row so far,
    // so one dropped loses nothing). A run stopped by a change of input or of the draft says so; one stopped by a new run on the page,
    // or by the page leaving, writes nothing more.
    private sealed class EveryStream(RunPlan plan, RunTicket ticket, TuningRuns runs, Render render, Names names, TuningQuery query) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            HttpResponse response = httpContext.Response;
            try
            {
                await Sse.Start(response);
                await Sse.PatchElements(response, await Html(TuningRuns.EveryStart(plan), null, null, 0));
                Channel<EverySnapshot> channel = Channel.CreateBounded<EverySnapshot>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
                Task<EverySnapshot> work = Task.Run(() =>
                {
                    try
                    {
                        return TuningRuns.RunEvery(plan, s => channel.Writer.TryWrite(s), ticket.Token);
                    }
                    finally
                    {
                        channel.Writer.TryComplete();
                    }
                });
                int sent = 0;
                await foreach (EverySnapshot snapshot in channel.Reader.ReadAllAsync())
                {
                    if (!ticket.Token.IsCancellationRequested)
                    {
                        sent = await Write(response, snapshot, null, sent);
                    }
                }

                EverySnapshot last = await work;
                if (ticket.Stop is RunStop.NewRun || httpContext.RequestAborted.IsCancellationRequested)
                {
                    return;
                }

                RunStop? stop = last.Finished ? null : ticket.Stop ?? RunStop.Left;
                await Write(response, last, stop, sent);
                if (stop is not null)
                {
                    // The rows that will not run now say so.
                    for (int i = last.Rows.Count; i < last.Objectives.Count; i++)
                    {
                        await Sse.PatchElements(response, await Html(last, stop, "row", i));
                    }
                }
            }
            finally
            {
                runs.End(ticket);
            }
        }

        // The progress line, then each row finished since the last write.
        private async Task<int> Write(HttpResponse response, EverySnapshot snapshot, RunStop? stop, int sent)
        {
            await Sse.PatchElements(response, await Html(snapshot, stop, "progress", 0));
            for (int i = sent; i < snapshot.Rows.Count; i++)
            {
                await Sse.PatchElements(response, await Html(snapshot, stop, "row", i));
            }

            return Math.Max(sent, snapshot.Rows.Count);
        }

        private Task<string> Html(EverySnapshot snapshot, RunStop? stop, string? part, int index)
        {
            return render.Html<EveryObjective>(new Dictionary<string, object?>
            {
                ["Snapshot"] = snapshot,
                ["Stop"] = stop,
                ["Names"] = names,
                ["Query"] = query,
                ["Part"] = part,
                ["Index"] = index,
            });
        }
    }

    // Each element morphed into the page by its id.
    private sealed class PatchesResult(IReadOnlyList<string> elements) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            await Sse.Start(httpContext.Response);
            foreach (string html in elements)
            {
                await Sse.PatchElements(httpContext.Response, html);
            }
        }
    }
}

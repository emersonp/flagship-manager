using Sim.Content;
using Sim.Core.Content;

namespace Web.Forms;

// What each form request does: open, create, save, show rows, and delete. Every change goes through ContentEditor.Save.
internal static class FormFlow
{
    public static ItemFormView Blank(KindForm form, ContentFileSet files)
    {
        FormChoices choices = FormChoicesReader.Read(files);
        FormValues values = form.Blank(choices);
        // A storylet or vignette is offered the next free id in the pack's pattern (1e part six); the author may change it before saving.
        if (!values.Has("id") && NewIds.Next(files, form.Kind) is { } suggested)
        {
            values = values.With("id", suggested);
        }

        return Build(form, files, choices, values.Get("id"), true, values, new Dictionary<string, string>(), [], null);
    }

    // The stored item's form; null when the id holds no item of this kind.
    public static ItemFormView? Open(KindForm form, ContentFileSet files, string id, string? notice = null)
    {
        FormChoices choices = form.Widen(FormChoicesReader.Read(files), files, id);
        return form.Load(files, id, choices) is { } values ? Build(form, files, choices, id, false, values, new Dictionary<string, string>(), [], notice) : null;
    }

    public static async Task<(ItemFormView View, bool Saved)> Create(KindForm form, ContentEditor editor, FormValues values, CancellationToken cancellation)
    {
        values = Renumbered(form, values);
        ContentFileSet files = editor.Read();
        FormChoices choices = FormChoicesReader.Read(files);
        string id = values.Get("id");
        // A trait left without an id takes one made from its name (1e part six, design 4.2).
        if (id.Length == 0 && form.Kind == ContentKind.Trait)
        {
            id = NewIds.FromName(values.Get("name"));
            values = values.With("id", id);
        }

        var errors = new Dictionary<string, string>();
        Checks.Id(id, files, errors, form.Kind);
        Bound bound = form.Bind(id, values, choices);
        foreach ((string field, string message) in bound.Errors)
        {
            errors[field] = message;
        }

        if (errors.Count > 0 || bound.Make is null)
        {
            return (Build(form, files, choices, id, true, values, errors, [], null), false);
        }

        // The check above is a quick answer; this one, under the save's lock, decides.
        SaveResult result = await editor.Save(f =>
        {
            if (Checks.UsedBy(f, id, form.Kind) is { } used)
            {
                throw new ChangeRefusedException($"That id is already used by {ContentKinds.WithArticle(used.Kind)}.");
            }

            return ContentEdits.Upsert(f, form.Kind, bound.Make(f));
        }, cancellation);
        if (!result.Saved)
        {
            return (Build(form, files, choices, id, true, values, new Dictionary<string, string>(), Reasons(result.Errors), null), false);
        }

        return (Open(form, editor.Read(), id, "Saved.") ?? Build(form, files, choices, id, false, values, new Dictionary<string, string>(), [], "Saved."), true);
    }

    // Null when the id holds no item of this kind.
    public static async Task<(ItemFormView View, bool Saved)?> Save(KindForm form, ContentEditor editor, string id, FormValues values, CancellationToken cancellation)
    {
        values = Renumbered(form, values);
        ContentFileSet files = editor.Read();
        FormChoices choices = form.Widen(FormChoicesReader.Read(files), files, id);
        if (form.Load(files, id, choices) is null)
        {
            return null;
        }

        // An id is fixed once saved (1e part six, review focus 5): the form shows it as text, so a post that names another is refused.
        if (values.Has("id") && values.Get("id") != id)
        {
            return (Build(form, files, choices, id, false, values, new Dictionary<string, string>(), [new ContentError("", $"The id is fixed once the item exists. It is {id}.")], null), false);
        }

        Bound bound = form.Bind(id, values, choices);
        if (bound.Errors.Count > 0 || bound.Make is null)
        {
            return (Build(form, files, choices, id, false, values, bound.Errors, [], null), false);
        }

        SaveResult result = await editor.Save(f =>
        {
            if (!f.Index().Any(i => i.Kind == form.Kind && i.Id == id))
            {
                throw new ChangeRefusedException("This item no longer exists.");
            }

            return ContentEdits.Upsert(f, form.Kind, bound.Make(f));
        }, cancellation);
        if (!result.Saved)
        {
            return (Build(form, files, choices, id, false, values, new Dictionary<string, string>(), Reasons(result.Errors), null), false);
        }

        ContentFileSet saved = editor.Read();
        ItemFormView view = Open(form, saved, id, "Saved.") ?? Build(form, saved, choices, id, false, values, new Dictionary<string, string>(), [], "Saved.");
        // The form is drawn from the file again, so the approach tab the author was on is carried over by hand.
        return (values.Has(EncounterKind.TabField) ? view with { Values = view.Values.With(EncounterKind.TabField, values.Get(EncounterKind.TabField)) } : view, true);
    }

    // The form drawn again from the posted fields with the row action applied; nothing is saved. Null for an unknown id.
    // The row buttons say what to do; a control that only changes what the form shows (the decision's box) names the element to focus.
    public static ItemFormView? Rows(KindForm form, ContentFileSet files, string? id, FormValues posted, string? focusId = null)
    {
        if (form.RowSets.Count == 0)
        {
            return null;
        }

        bool isNew = id is null;
        FormChoices choices = isNew ? FormChoicesReader.Read(files) : form.Widen(FormChoicesReader.Read(files), files, id!);
        if (!isNew && form.Load(files, id!, choices) is null)
        {
            return null;
        }

        // "Write one for here": a line of the item's own for that beat, started from what plays there now.
        string action = posted.Get(RowSet.ActionField);
        if (action.StartsWith(OwnedText.WritePrefix, StringComparison.Ordinal) && form.RowSets.Contains(OwnedText.Lines))
        {
            string beat = action[OwnedText.WritePrefix.Length..];
            List<Dictionary<string, string>> lines = FormRows.Read(posted, OwnedText.Lines);
            Dictionary<string, string> line = OwnedText.Lines.NewRow();
            line["beat"] = beat;
            line["text"] = Plays.FallbackText(form.Kind, files, id ?? "", posted, choices, beat);
            lines.Add(line);
            FormValues written = FormRows.Write(posted, OwnedText.Lines, lines);
            return Build(form, files, choices, isNew ? posted.Get("id") : id!, isNew, written, new Dictionary<string, string>(), [], null) with { FocusId = FormValues.Slug(OwnedText.Lines.Name(lines.Count - 1, "text")) };
        }

        FormRows.Applied applied = FormRows.Apply(posted, form.RowSets);
        // Add: the new row's first field. Move: the moved row's first field, so the author sees where it went. Remove: that list's Add button.
        string? focus = applied.Set is not { } set
            ? SafeFocus(focusId)
            : applied.Verb == "remove" ? set.AddId : FormValues.Slug(set.Name(applied.Row, set.Fields[0]));
        return Build(form, files, choices, isNew ? posted.Get("id") : id!, isNew, form.AfterRows(applied), new Dictionary<string, string>(), [], null) with { FocusId = focus };
    }

    // The form as posted, for its panel's previews; sample, when given, is the sample to show. Null for an unknown id or a kind with no panel.
    public static ItemFormView? Previews(KindForm form, ContentFileSet files, string? id, FormValues posted, string? sample)
    {
        bool isNew = id is null;
        FormChoices choices = isNew ? FormChoicesReader.Read(files) : form.Widen(FormChoicesReader.Read(files), files, id!);
        if (!isNew && form.Load(files, id!, choices) is null)
        {
            return null;
        }

        FormValues values = sample is { Length: > 0 and <= 4 } && sample.All(char.IsAsciiDigit) ? posted.With("sample", sample) : posted;
        ItemFormView view = Build(form, files, choices, isNew ? posted.Get("id") : id!, isNew, values, new Dictionary<string, string>(), [], null);
        return view.Plays is null ? null : view;
    }

    // An element id as the editor makes them; anything else is no focus.
    private static string? SafeFocus(string? id)
    {
        return id is not null && System.Text.RegularExpressions.Regex.IsMatch(id, "^f-[A-Za-z0-9_-]{1,80}$") ? id : null;
    }

    public static async Task<DeleteOutcome> Delete(KindForm form, ContentEditor editor, string id, CancellationToken cancellation)
    {
        ContentFileSet files = editor.Read();
        if (Open(form, files, id) is not { } view)
        {
            return new DeleteOutcome(false, false, null);
        }

        // A skill's delete also takes it from the starting crew: the one save declared to change more than one file.
        SaveResult result = form.Kind == ContentKind.Skill
            ? await editor.SaveAcrossFiles(f => ContentEdits.DeleteSkill(f, id), ContentEditor.CrewFirst, cancellation)
            : await editor.Save(f => ContentEdits.Delete(f, form.Kind, id), cancellation);
        return result.Saved
            ? new DeleteOutcome(true, true, null)
            : new DeleteOutcome(true, false, view with { Reasons = Reasons(result.Errors) });
    }

    private static ItemFormView Build(KindForm form, ContentFileSet files, FormChoices choices, string id, bool isNew, FormValues values, IReadOnlyDictionary<string, string> errors, IReadOnlyList<ContentError> reasons, string? notice)
    {
        string? name = isNew ? null : form.NameOf(files, id);
        (string? lead, IReadOnlyList<string> warnings) = isNew ? (null, []) : Warning(files, form.Kind, id);
        // A refusal shows the loss messages in the alert, so the warning above does not repeat them.
        IReadOnlyList<string> shown = reasons.Count > 0 ? [] : warnings;
        // Focus goes to the first field that failed its check, or to the alert for what the loader or the save said.
        string? focus = errors.Count > 0 ? FieldId(form.Kind, errors.Keys.First()) : reasons.Count > 0 ? "form-alert" : null;
        return new ItemFormView(form.Kind, isNew, id, name, values, errors, reasons, notice, lead, shown, choices, focus)
        {
            DeleteNote = isNew ? null : DeleteNote(files, form.Kind, id),
            TextHref = lead is null ? null : TextHref(files, form.Kind, id),
            Preview = form.Kind == ContentKind.Line ? LinePreview.Of(files, id, values) : null,
            Plays = Plays.Of(form.Kind, files, id, values, choices),
        };
    }

    // The text view of the item's file, where content the form cannot keep can be edited; null when the file has none.
    private static string? TextHref(ContentFileSet files, ContentKind kind, string id)
    {
        return files.Index().FirstOrDefault(i => i.Kind == kind && i.Id == id) is { } item ? TextFile.All.FirstOrDefault(f => f.Path == item.File)?.Action : null;
    }

    private static string? DeleteNote(ContentFileSet files, ContentKind kind, string id)
    {
        if (kind == ContentKind.Skill)
        {
            int holders = files.Index().Where(i => i.Kind == ContentKind.Crew).Count(i => ContentEdits.Get<CrewModel>(files, i.Kind, i.Id)?.Skills.ContainsKey(id) == true);
            return holders switch
            {
                0 => null,
                1 => "This also removes it from 1 starting crew member.",
                _ => $"This also removes it from {holders} starting crew.",
            };
        }

        // The missions module skips an objective whose template is gone, so a legacy keeps playing.
        return kind == ContentKind.Objective ? "Legacies whose current site offers it will skip it." : null;
    }

    // An assignment's tags are boxes numbered in the page; every other field's id comes from its name.
    private static string FieldId(ContentKind kind, string name)
    {
        return kind == ContentKind.Assignment && name == "tags" ? "f-tags-0" : FormValues.Slug(name);
    }

    // Rows posted with gaps in their indexes (offer[0], offer[5]) are numbered from 0 before they are bound, so an error names a field on the page.
    private static FormValues Renumbered(KindForm form, FormValues values)
    {
        return form.RowSets.Aggregate(values, (v, rows) => FormRows.Write(v, rows, FormRows.Read(v, rows)));
    }

    // A loss in the item's own file: its own (saving replaces it) or another item's (saving is refused until that is fixed).
    private static (string? Lead, IReadOnlyList<string> Warnings) Warning(ContentFileSet files, ContentKind kind, string id)
    {
        if (files.Index().FirstOrDefault(i => i.Kind == kind && i.Id == id) is not { } item)
        {
            return (null, []);
        }

        IReadOnlyList<ContentLoss> losses = ContentEdits.Losses(files, item.File);
        if (item.Lossy)
        {
            return ("This item has content the editor cannot keep. Saving replaces it with what the form shows.", losses.Select(l => l.ToString()).ToList());
        }

        if (losses.Count == 0)
        {
            return (null, []);
        }

        // A file-level loss (an inner comment, a second document) belongs to no item, so the lead names the file.
        string lead = losses.All(l => l.Item is null)
            ? "This file has content the editor cannot keep, so a save here is refused until it is fixed by hand."
            : "Another item in this file has content the editor cannot keep, so a save here is refused until it is fixed by hand.";
        return (lead, losses.Select(l => l.ToString()).ToList());
    }

    // A loss message already starts with its file; the list shows the file once.
    private static List<ContentError> Reasons(IReadOnlyList<ContentError> errors)
    {
        return errors.Select(e => e.File.Length > 0 && e.Message.StartsWith(e.File + ": ", StringComparison.Ordinal) ? new ContentError(e.File, e.Message[(e.File.Length + 2)..]) : e).ToList();
    }
}

internal sealed record DeleteOutcome(bool Found, bool Deleted, ItemFormView? Refused);

using Sim.Content;
using Sim.Core.Content;

namespace Web.Forms;

// What each form request does: open, create, save, show rows, and delete. Every change goes through ContentEditor.Save.
internal static class FormFlow
{
    public static ItemFormView Blank(KindForm form, ContentFileSet files)
    {
        FormChoices choices = FormChoicesReader.Read(files);
        return Build(form, files, choices, "", true, form.Blank(choices), new Dictionary<string, string>(), [], null);
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
        var errors = new Dictionary<string, string>();
        Checks.Id(id, files, errors);
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
            if (f.Index().FirstOrDefault(i => i.Id == id) is { } used)
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
        return (Open(form, saved, id, "Saved.") ?? Build(form, saved, choices, id, false, values, new Dictionary<string, string>(), [], "Saved."), true);
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

        FormRows.Applied applied = FormRows.Apply(posted, form.RowSets);
        // Add: the new row's first field. Move: the moved row's first field, so the author sees where it went. Remove: that list's Add button.
        string? focus = applied.Set is not { } set
            ? SafeFocus(focusId)
            : applied.Verb == "remove" ? set.AddId : FormValues.Slug(set.Name(applied.Row, set.Fields[0]));
        return Build(form, files, choices, isNew ? posted.Get("id") : id!, isNew, applied.Values, new Dictionary<string, string>(), [], null) with { FocusId = focus };
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

        SaveResult result = await editor.Save(f => ContentEdits.Delete(f, form.Kind, id), cancellation);
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
        return new ItemFormView(form.Kind, isNew, id, name, values, errors, reasons, notice, lead, shown, choices, focus);
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

        return losses.Count > 0
            ? ("Another item in this file has content the editor cannot keep, so a save here is refused until it is fixed by hand.", losses.Select(l => l.ToString()).ToList())
            : (null, []);
    }

    // A loss message already starts with its file; the list shows the file once.
    private static List<ContentError> Reasons(IReadOnlyList<ContentError> errors)
    {
        return errors.Select(e => e.File.Length > 0 && e.Message.StartsWith(e.File + ": ", StringComparison.Ordinal) ? new ContentError(e.File, e.Message[(e.File.Length + 2)..]) : e).ToList();
    }
}

internal sealed record DeleteOutcome(bool Found, bool Deleted, ItemFormView? Refused);

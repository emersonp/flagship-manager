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

        SaveResult result = await editor.Save(f => ContentEdits.Upsert(f, form.Kind, bound.Make(f)), cancellation);
        if (!result.Saved)
        {
            return (Build(form, files, choices, id, true, values, new Dictionary<string, string>(), Reasons(result.Errors), null), false);
        }

        return (Open(form, editor.Read(), id, "Saved.") ?? Build(form, files, choices, id, false, values, new Dictionary<string, string>(), [], "Saved."), true);
    }

    // Null when the id holds no item of this kind.
    public static async Task<(ItemFormView View, bool Saved)?> Save(KindForm form, ContentEditor editor, string id, FormValues values, CancellationToken cancellation)
    {
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

        SaveResult result = await editor.Save(f => ContentEdits.Upsert(f, form.Kind, bound.Make(f)), cancellation);
        if (!result.Saved)
        {
            return (Build(form, files, choices, id, false, values, new Dictionary<string, string>(), Reasons(result.Errors), null), false);
        }

        ContentFileSet saved = editor.Read();
        return (Open(form, saved, id, "Saved.") ?? Build(form, saved, choices, id, false, values, new Dictionary<string, string>(), [], "Saved."), true);
    }

    // The form drawn again from the posted fields with the row action applied; nothing is saved. Null for an unknown id.
    public static ItemFormView? Rows(KindForm form, ContentFileSet files, string? id, FormValues posted)
    {
        if (form.Rows is not { } rows)
        {
            return null;
        }

        bool isNew = id is null;
        FormChoices choices = isNew ? FormChoicesReader.Read(files) : form.Widen(FormChoicesReader.Read(files), files, id!);
        if (!isNew && form.Load(files, id!, choices) is null)
        {
            return null;
        }

        return Build(form, files, choices, isNew ? posted.Get("id") : id!, isNew, FormRows.Apply(posted, rows), new Dictionary<string, string>(), [], null);
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
        return new ItemFormView(form.Kind, isNew, id, name, values, errors, reasons, notice, lead, warnings, choices);
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

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Sim.Content;
using Sim.Core.Content;

namespace Web.Forms;

// The ids a new item is offered (1e part six, design 4.2). A storylet or vignette is offered the next free number in the pack's
// pattern (STORYLET_09); the author may change it before the first save. A trait's id is made from its name. Every id locks once it is
// saved: the form shows it as text, and a save that posts another is refused.
internal static class NewIds
{
    // The prefix a kind's ids are numbered under; null for a kind whose ids are not numbered.
    private static string? Prefix(ContentKind kind)
    {
        return kind switch
        {
            ContentKind.Storylet => "STORYLET",
            ContentKind.Vignette => "VIGNETTE",
            _ => null,
        };
    }

    // The next number after the highest of the kind's PREFIX_NN ids, at the width they are written (two digits when none is), and free
    // in the namespace the kind's ids share; null for a kind with no pattern.
    public static string? Next(ContentFileSet files, ContentKind kind)
    {
        if (Prefix(kind) is not { } prefix)
        {
            return null;
        }

        var pattern = new Regex($"^{prefix}_([0-9]+)$", RegexOptions.CultureInvariant);
        List<Match> numbered = files.Index().Where(i => i.Kind == kind).Select(i => pattern.Match(i.Id)).Where(m => m.Success).ToList();
        int width = numbered.Count == 0 ? 2 : numbered.Max(m => m.Groups[1].Length);
        int number = numbered.Select(m => int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0).DefaultIfEmpty(0).Max();
        while (true)
        {
            number++;
            string id = $"{prefix}_{number.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}";
            if (!Ids.IsItemId(id))
            {
                return null;
            }

            if (Checks.UsedBy(files, id, kind) is null)
            {
                return id;
            }
        }
    }

    private static readonly HashSet<string> Articles = new(["a", "an", "the"], StringComparer.Ordinal);

    // A lower-case id from a name, as docs/conventions.md has them: letters and digits, words joined by underscores, the articles
    // dropped ("Hold a service" is hold_service), letters with marks kept without them ("Pelpheär" is pelphear), and at most 40 in all.
    // Empty when the name has no letter to start with.
    public static string FromName(string name)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (char c in name.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                word.Append(c);
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c != '\'' && c != '’')
            {
                Flush();
            }
        }

        Flush();
        List<string> kept = words.Where(w => !Articles.Contains(w)).ToList();
        string id = string.Join("_", kept.Count > 0 ? kept : words).TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '_');
        id = id.Length > 40 ? id[..40].TrimEnd('_') : id;
        return Ids.IsInnerId(id) ? id : "";

        void Flush()
        {
            if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }
    }
}

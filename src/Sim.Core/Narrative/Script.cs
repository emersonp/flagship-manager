using System.Text.RegularExpressions;

namespace Sim.Core.Narrative;

public enum EntryKind
{
    Narration,
    Speech,
    Note,
}

// One line of a script. Speaker is set for speech only: "captain", a person role of the beat ("lead", "actor"), or
// "best SKILL_ID", the team member still standing who is best at that skill and is not the lead.
public sealed record ScriptEntry(EntryKind Kind, string? Speaker, string Text);

// A line's text read as a script: its entries in order, and the one sentence the report uses in their place, if it gives one.
public sealed record Script(IReadOnlyList<ScriptEntry> Entries, string? Summary)
{
    // What the report can say for this script: its summary, else the script itself when it is a single sentence of narration.
    public string? ReportText => Summary ?? (Entries is [{ Kind: EntryKind.Narration } only] ? only.Text : null);
}

// The script grammar of line text (GDD section 6). Each line of the text is one entry:
//   lead: Captain, we are here.      speech, when the line starts with a speaker and a colon
//   > No life signs detected.        a system note
//   = The team reached the relay.    the report's sentence; at most one, and last
//   anything else                    narration
// A text that is only "-" says nothing: the beat passes in silence, and no shared line stands in.
// One sentence with no line break is a script of one narration entry, which is what every line was before scripts.
public static partial class Scripts
{
    public const string Captain = "captain";
    public const string BestPrefix = "best ";
    public const string Silence = "-";

    // The person roles any beat can have; a speaker must be one the line's own beat has.
    public static readonly IReadOnlyList<string> Roles = ["lead", "actor", "helper", "crew", "subject"];

    public static Script Parse(string text)
    {
        if (text.Trim() == Silence)
        {
            return new Script([], null);
        }

        var entries = new List<ScriptEntry>();
        string? summary = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (summary is not null)
            {
                throw new NarrativeFormatException("The report's sentence (the line starting with =) must be the last line.");
            }

            if (line[0] == '=')
            {
                summary = Rest(line, "The report's sentence after = is empty.");
            }
            else if (line[0] == '>')
            {
                entries.Add(new ScriptEntry(EntryKind.Note, null, Rest(line, "The note after > is empty.")));
            }
            else if (SpeechPattern().Match(line) is { Success: true } speech)
            {
                entries.Add(new ScriptEntry(EntryKind.Speech, speech.Groups[1].Value, speech.Groups[2].Value));
            }
            else
            {
                entries.Add(new ScriptEntry(EntryKind.Narration, null, line));
            }
        }

        if (entries.Count == 0)
        {
            throw new NarrativeFormatException(summary is null ? "The text is empty." : "A script needs at least one line besides the report's sentence. Write - alone for a beat that says nothing.");
        }

        return new Script(entries, summary);
    }

    // The skill a "best SKILL_ID" speaker names; null for any other speaker.
    public static string? BestSkill(string speaker)
    {
        return speaker.StartsWith(BestPrefix, StringComparison.Ordinal) ? speaker[BestPrefix.Length..] : null;
    }

    private static string Rest(string line, string problem)
    {
        string rest = line[1..].Trim();
        return rest.Length > 0 ? rest : throw new NarrativeFormatException(problem);
    }

    [GeneratedRegex(@"^(captain|lead|actor|helper|crew|subject|best [A-Za-z][A-Za-z0-9_]*):\s+(\S.*)$")]
    private static partial Regex SpeechPattern();
}

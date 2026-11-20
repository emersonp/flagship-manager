namespace Sim.Core;

// The kinds of chronicle entry (slice 1e part five, design 4.3), as HistoryKinds are the history's.
public static class ChronicleKinds
{
    public const string Founding = "founding";
    public const string Death = "death";
    public const string Mission = "mission";
    public const string Call = "call";
    public const string Storylet = "storylet";

    public static readonly IReadOnlyList<string> All = [Founding, Death, Mission, Call, Storylet];
}

// A crew member an entry is about, with the name they had when it was written: frozen, as the text is (design 4.2).
public sealed record ChronicleSubject(string Id, string Name);

// One entry (design 4.2): written once, when what it records happens, and never changed. Id: chr_ and a counter per legacy. Text:
// rendered from the pack live at the moment of writing. SourceSeq: the sequence numbers of the events it records.
public sealed record ChronicleEntry(string Id, int Day, int Cycle, string Kind, int Weight, IReadOnlyList<ChronicleSubject> Subjects, string Text, IReadOnlyList<long> SourceSeq);

// tuning/chronicle.yaml (design 4.4): the weight of each kind of entry, by key, and the least weight Notable shows. A weight of 0
// writes no entry.
public sealed record ChronicleTuning(IReadOnlyDictionary<string, int> Weights, int Notable)
{
    public const string Founding = "founding";
    public const string Death = "death";
    public const string Storylet = "storylet";
    public const string CallOverruled = "callOverruled";
    public const string CallBacked = "callBacked";
    public const string MissionLost = "missionLost";
    public const string MissionWon = "missionWon";

    // The most an entry can weigh, and the most notable can be.
    public const int Most = 5;

    // Every key the file must have, in the order the file writes them.
    public static readonly IReadOnlyList<string> Keys = [Founding, Death, Storylet, CallOverruled, CallBacked, MissionLost, MissionWon];

    // The design's numbers, for a pack built in code.
    public static readonly ChronicleTuning Default = new(
        new Dictionary<string, int>
        {
            [Founding] = 5,
            [Death] = 5,
            [Storylet] = 3,
            [CallOverruled] = 3,
            [CallBacked] = 2,
            [MissionLost] = 2,
            [MissionWon] = 1,
        },
        3);
}

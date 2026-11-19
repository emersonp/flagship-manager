namespace Sim.Core.Content;

// A word for a handler's chance on a storylet's check (1e part four, design 4.7), worked out from the dice. The bridge shows it and
// the tests read it through this one function.
public enum Chance
{
    LongShot,
    Even,
    Likely,
    Sure,
}

public static class Chances
{
    // The chance that score + the dice reaches against: below 35% a long shot, below 65% even, below 95% likely, else sure. On 2d6
    // that is sure when the dice need 3 or less, likely 4 to 6, even 7 or 8, and a long shot 9 or more.
    public static Chance Of(DiceDef dice, int score, int against)
    {
        double p = dice.AtLeast(against - score);
        return p < 0.35 ? Chance.LongShot : p < 0.65 ? Chance.Even : p < 0.95 ? Chance.Likely : Chance.Sure;
    }

    public static string Word(Chance chance)
    {
        return chance switch
        {
            Chance.LongShot => "long shot",
            Chance.Even => "even",
            Chance.Likely => "likely",
            _ => "sure",
        };
    }
}

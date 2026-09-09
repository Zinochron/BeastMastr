namespace BeastMastr.Rules;

/// <summary>
/// Three-letter tags for the statuses.
///
/// A bestiary tile is fifty pixels square with the beast's number already under it, so the full
/// label does not fit and two of them certainly do not. These are only ever shown next to the game's
/// own window, where the reader has the full names a click away.
/// </summary>
public static class StatusAbbreviations
{
    public static string Short(this BeastStatus status) => status switch
    {
        BeastStatus.Slow => "SLW",
        BeastStatus.PetrificationOrFreeze => "PET",
        BeastStatus.Paralysis => "PAR",
        BeastStatus.Interruption => "INT",
        BeastStatus.Blind => "BLD",
        BeastStatus.Poison => "PSN",
        BeastStatus.Stun => "STN",
        BeastStatus.Sleep => "SLP",
        BeastStatus.Bind => "BND",
        BeastStatus.Heavy => "HVY",
        BeastStatus.FlatDamageOrDeath => "DTH",
        _ => "?",
    };

    /// <summary>
    /// Cleanse and dispel are one beast each out of fifty, so they are worth a tag of their own even
    /// though they are not statuses.
    /// </summary>
    public static string? Short(this BeastTrait trait) => trait switch
    {
        BeastTrait.Cleanse => "CLR",
        BeastTrait.Dispel => "DSP",
        _ => null,
    };
}

using System.Collections.Generic;

namespace BeastMastr.Rules;

/// <summary>
/// The eleven statuses a beast's actions can inflict, in the order <c>BNpcResist</c> uses — the
/// same order <c>XBMPet</c>'s eleven bool columns follow, so slot n is column 11 + n.
///
/// The names are the game's own: the board detail window lists all eleven as a legend, and every
/// one of them lines up with a slot that had already been identified by correlating the beasts
/// setting it against their action descriptions. See <c>README-DEV.md</c>.
/// </summary>
public enum BeastStatus
{
    Slow = 0,
    PetrificationOrFreeze = 1,
    Paralysis = 2,

    /// <summary>
    /// The one the plugin exists for, and the one that was guessed wrong before the legend turned
    /// up — it had been inferred as Silence. Slot 3 is set by coblyn, dullahan, golem, spriggan and
    /// ice golem, none of which interrupts in either of the two action descriptions the sheet
    /// carries, which fits: their interrupt is the Borrow the sheet does not hold.
    /// </summary>
    Interruption = 3,

    Blind = 4,
    Poison = 5,
    Stun = 6,
    Sleep = 7,
    Bind = 8,

    /// <summary>Heavy, and also sicken — goobbue sickens and sits here, as do the beasts that inflict heaviness.</summary>
    Heavy = 9,

    /// <summary>Doom and the like: ghost and rafflesia doom, Karlabos cuts HP to a single digit.</summary>
    FlatDamageOrDeath = 10,
}

public static class BeastStatusNames
{
    /// <summary>The labels the game uses, so nothing here invents a word the player has not seen.</summary>
    public static string Label(this BeastStatus status) => status switch
    {
        BeastStatus.Slow => "Slow",
        BeastStatus.PetrificationOrFreeze => "Petrification/Freeze",
        BeastStatus.Paralysis => "Paralysis",
        BeastStatus.Interruption => "Interruption",
        BeastStatus.Blind => "Blind",
        BeastStatus.Poison => "Poison",
        BeastStatus.Stun => "Stun",
        BeastStatus.Sleep => "Sleep",
        BeastStatus.Bind => "Bind",
        BeastStatus.Heavy => "Heavy",
        BeastStatus.FlatDamageOrDeath => "Flat Damage/Death",
        _ => status.ToString(),
    };

    /// <summary>
    /// The order the game shows them in, which is not the order it stores them in: the legend runs
    /// down the slots but puts Poison at the end rather than at 5. Anything the player reads should
    /// follow this, so a badge row matches the window they already know.
    /// </summary>
    public static readonly IReadOnlyList<BeastStatus> DisplayOrder =
    [
        BeastStatus.Slow,
        BeastStatus.PetrificationOrFreeze,
        BeastStatus.Paralysis,
        BeastStatus.Interruption,
        BeastStatus.Blind,
        BeastStatus.Stun,
        BeastStatus.Sleep,
        BeastStatus.Bind,
        BeastStatus.Heavy,
        BeastStatus.FlatDamageOrDeath,
        BeastStatus.Poison,
    ];
}

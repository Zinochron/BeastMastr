using System.Collections.Generic;

namespace BeastMastr.Rules;

/// <summary>
/// The items worth using in a board's final fight, by <c>XBMItem</c> row, once the Battlehorns are out (the
/// user: "every item may be used in the final fight, only not so early that it pulls before the horns").
/// Each buff goes once per fight; the antidote whenever the player is poisoned.
///
/// Left out on purpose: the feral potions (each also stuns, blinds, petrifies or puts the user to sleep),
/// the smokebomb (it leaves the fight), Spellforge and Steelsting tomes (they change every attack's damage
/// type), Temporal Sand (Sandstill), the Thief's and Merchant's Eyes (for the next enemy space, not a boss),
/// the needle and the Blessed Horn (for familiars), and the healing items, which go by HP as ever.
/// </summary>
public static class BossItems
{
    public const uint BeastPotionKit = 140;
    public const uint Antidote = 83;

    /// <summary>Once per fight, in this order: the Beast Potion Kit first, then what keeps the player up.</summary>
    public static readonly uint[] Buffs =
    [
        BeastPotionKit,
        99, 98,             // G2, G1 Beastmaster Reraiser
        87, 86,             // G2, G1 Antipoison Soul Serum: Borgny poisons
        141,                // Beast Remedy Kit
        102, 103,           // Crucible Tannin, Crucible Stimulant
        104, 106, 108, 110, // Potions of Tempered Strength, Magic, Intensity, Evasion
        112, 114,           // Potion of Tempered Constitution, of Breathtaking Swiftness
        135, 136, 137,      // Vampiric Essence, Tome of Reflection, Tome of the Impervious
        115,                // Crucible Feather: TP
        117, 116, 119, 118, 121, 120, 123, 122, 125, 124, 127, 126, // the weakeners, G2 first
    ];

    /// <summary>Poisons the antidote cures: Borgny's Toxicosis, all three of its forms.</summary>
    public static readonly IReadOnlySet<uint> Poisons = new HashSet<uint> { 5183, 5553, 5554 };

    /// <summary>The item to use next, or null.</summary>
    /// <param name="held">The rows held.</param>
    /// <param name="used">The rows already used, or tried, this fight.</param>
    /// <param name="statuses">The player's statuses.</param>
    public static uint? Next(IReadOnlyCollection<uint> held, IReadOnlySet<uint> used, IReadOnlySet<uint> statuses)
    {
        var have = new HashSet<uint>(held);

        if (have.Contains(Antidote))
        {
            foreach (var status in statuses)
            {
                if (Poisons.Contains(status))
                    return Antidote;
            }
        }

        foreach (var row in Buffs)
        {
            if (have.Contains(row) && !used.Contains(row))
                return row;
        }

        return null;
    }
}

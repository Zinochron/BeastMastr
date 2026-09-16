using System;
using System.Collections.Generic;

namespace BeastMastr.Rules;

/// <summary>The Beastmaster's actions and statuses by id, as the <c>Action</c> and <c>Status</c> sheets have them.</summary>
public static class Bst
{
    public const uint SmashAxe = 44879;
    public const uint AxebladeBite = 44883;
    public const uint Shieldsplitter = 44885;

    public const uint AvalancheAxe = 44884;
    public const uint MistralAxe = 44887;
    public const uint SpinningAxe = 44888;
    public const uint GaleAxe = 44889;

    public const uint FirstBattlehorn = 44881;
    public const uint SecondBattlehorn = 44892;
    public const uint ThirdBattlehorn = 44894;

    public const uint BeastMode = 44886;
    public const uint TemperedRelease = 44890;
    public const uint PartingBlow = 44891;
    public const uint ShieldCharge = 44893;
    public const uint Borrow = 44895;
    public const uint RallyingCheer = 44904;
    public const uint Rally = 44905;
    public const uint Trick = 47093;

    public const uint VolantHeart = 4595;
    public const uint RampantHeart = 4596;
    public const uint DurantHeart = 4597;
    public const uint EldritchHeart = 4598;
    public const uint Sunstrider = 4599;
    public const uint Moonstalker = 4600;
    public const uint OneWithNature = 4601;
    public const uint WaveringHeart = 4643;

    /// <summary>Soul Kinship, both variants: its Beast Mode interrupts, so it is kept for a cast.</summary>
    public static readonly IReadOnlySet<uint> SoulKinship = new HashSet<uint> { 4608, 4650 };

    /// <summary>Every Kinship, the timed ones and the permanent ones.</summary>
    public static readonly IReadOnlySet<uint> Kinships = Range(4602, 4609, 4644, 4651);

    public static readonly uint[] Battlehorns = [FirstBattlehorn, SecondBattlehorn, ThirdBattlehorn];

    /// <summary>Levels, from the sheet. The plugin checks them against the game data as it loads.</summary>
    public static readonly IReadOnlyDictionary<uint, int> Levels = new Dictionary<uint, int>
    {
        [SmashAxe] = 1, [AxebladeBite] = 2, [Shieldsplitter] = 12,
        [AvalancheAxe] = 4, [MistralAxe] = 8, [SpinningAxe] = 14, [GaleAxe] = 16,
        [FirstBattlehorn] = 1, [SecondBattlehorn] = 10, [ThirdBattlehorn] = 20,
        [PartingBlow] = 6, [Trick] = 8, [TemperedRelease] = 18, [BeastMode] = 22, [Borrow] = 22,
        [ShieldCharge] = 24, [Rally] = 28, [RallyingCheer] = 40,
    };

    /// <summary>The TP an axe needs at least. At 250 the level 50 trait turns them into their big forms.</summary>
    public const int AxeMinimumTp = 100;

    public const int FamiliarActionTp = 100;

    private static HashSet<uint> Range(uint from1, uint to1, uint from2, uint to2)
    {
        var set = new HashSet<uint>();
        for (var id = from1; id <= to1; id++)
            set.Add(id);

        for (var id = from2; id <= to2; id++)
            set.Add(id);

        return set;
    }
}

/// <param name="Combo">Press the three-step combo. Off while BossMod plays the rotation.</param>
/// <param name="Resources">Spend TP, familiar TP and cooldowns.</param>
/// <param name="SpendTpAt">With no Heart to pair with, an axe is used once TP reaches this, not before.</param>
/// <param name="UsePartingBlow">Send the familiar off with Parting Blow to summon the next one — its cooldowns reset.</param>
/// <param name="UseShieldCharge">Close a gap with Shield Charge.</param>
public sealed record BstOptions(bool Combo, bool Resources, int SpendTpAt, bool UseBattlehorns, bool UsePartingBlow,
                                bool UseShieldCharge)
{
    public static BstOptions Default => new(true, true, 200, true, false, true);
}

/// <summary>Everything the rotation looks at, taken once per decision.</summary>
/// <param name="Ready">Whether an action can be used right now — the game's own answer, cooldown and all.</param>
/// <param name="TargetDistance">To the target's edge, in yalms; infinity without a target.</param>
public sealed record BstState(
    int Level,
    uint ComboAction,
    float ComboTimer,
    int PlayerTp,
    int FamiliarTp,
    IReadOnlySet<uint> Statuses,
    Func<uint, bool> Ready,
    bool InCombat,
    bool HasTarget,
    float TargetDistance,
    bool TargetCasting,
    bool FamiliarOut);

/// <param name="Gcd">The weaponskill to press, or 0.</param>
/// <param name="Ogcd">The ability to press alongside it, or 0.</param>
public sealed record BstDecision(uint Gcd, uint Ogcd, string Why);

/// <summary>
/// What a Beastmaster presses next. Pure: it is handed a snapshot and says two ids.
///
/// The combo builds TP; the axes spend it. What makes an axe worth pressing is the Heart the familiar's
/// Trick leaves behind — an axe whose affinity follows that Heart completes an intentional combo, and a
/// skill of the opposite star after that completes the infinitive one. So Trick goes out when there is no
/// Heart to pair with, the axe that follows goes out while there is, and TP is only spent without a Heart
/// once it is high enough not to waste the potency that grows with it.
/// </summary>
public static class BstRotation
{
    private const float MeleeRange = 3f;

    public static BstDecision Next(BstState state, BstOptions options)
    {
        var why = new List<string>();
        var ogcd = options.Resources ? Ability(state, options, why) : 0u;
        var gcd = options.Combo && state.HasTarget && state.TargetDistance <= MeleeRange ? Combo(state, why) : 0u;
        return new BstDecision(gcd, ogcd, string.Join("; ", why));
    }

    private static uint Combo(BstState state, List<string> why)
    {
        if (state.ComboTimer > 0f && state.ComboAction == Bst.AxebladeBite && Has(state, Bst.Shieldsplitter))
        {
            why.Add("combo 3");
            return Bst.Shieldsplitter;
        }

        if (state.ComboTimer > 0f && state.ComboAction == Bst.SmashAxe && Has(state, Bst.AxebladeBite))
        {
            why.Add("combo 2");
            return Bst.AxebladeBite;
        }

        why.Add("combo 1");
        return Bst.SmashAxe;
    }

    private static uint Ability(BstState state, BstOptions options, List<string> why)
    {
        if (!state.InCombat)
            return 0;

        // A familiar in the fight is what every other ability here works through.
        if (options.UseBattlehorns && !state.FamiliarOut)
        {
            foreach (var horn in Bst.Battlehorns)
            {
                if (Usable(state, horn))
                {
                    why.Add("summon a familiar");
                    return horn;
                }
            }
        }

        if (!state.HasTarget)
            return 0;

        if (state.Statuses.Contains(Bst.OneWithNature) && Usable(state, Bst.TemperedRelease))
        {
            why.Add("One with Nature: Tempered Release");
            return Bst.TemperedRelease;
        }

        if (state.FamiliarOut && Usable(state, Bst.Borrow))
        {
            why.Add("Borrow");
            return Bst.Borrow;
        }

        var axe = Axe(state, options, why);
        if (axe != 0)
            return axe;

        var heart = Heart(state);
        if (heart == 0 && !state.Statuses.Contains(Bst.WaveringHeart) && state.FamiliarOut &&
            state.FamiliarTp >= Bst.FamiliarActionTp && Usable(state, Bst.Trick))
        {
            why.Add("Trick for a Heart");
            return Bst.Trick;
        }

        if (state.PlayerTp <= 150 && Usable(state, Bst.Rally))
        {
            why.Add("Rally for TP");
            return Bst.Rally;
        }

        if (state.FamiliarOut && state.FamiliarTp <= 150 && Usable(state, Bst.RallyingCheer))
        {
            why.Add("Rallying Cheer for familiar TP");
            return Bst.RallyingCheer;
        }

        if (HasKinship(state) && Usable(state, Bst.BeastMode) &&
            (!Bst.SoulKinship.Overlaps(state.Statuses) || state.TargetCasting))
        {
            why.Add("Beast Mode");
            return Bst.BeastMode;
        }

        if (options.UsePartingBlow && state.FamiliarOut && state.Level >= 30 &&
            !Usable(state, Bst.TemperedRelease) && !Usable(state, Bst.Borrow) && Usable(state, Bst.PartingBlow))
        {
            why.Add("Parting Blow, to summon the next familiar");
            return Bst.PartingBlow;
        }

        if (options.UseShieldCharge && state.TargetDistance > MeleeRange + 1f && state.TargetDistance <= 20f &&
            Usable(state, Bst.ShieldCharge))
        {
            why.Add("Shield Charge to close in");
            return Bst.ShieldCharge;
        }

        return 0;
    }

    /// <summary>
    /// The axe to press: the one that completes a combo with the Heart or star that is up, else the
    /// highest one known once TP is high enough, else none.
    /// </summary>
    private static uint Axe(BstState state, BstOptions options, List<string> why)
    {
        if (state.PlayerTp < Bst.AxeMinimumTp || state.TargetDistance > MeleeRange)
            return 0;

        uint[] pairing = Heart(state) switch
        {
            Bst.VolantHeart => [Bst.AvalancheAxe],
            Bst.RampantHeart => [Bst.MistralAxe],
            Bst.DurantHeart => [Bst.SpinningAxe],
            Bst.EldritchHeart => [Bst.GaleAxe],
            _ when state.Statuses.Contains(Bst.Sunstrider) => [Bst.MistralAxe, Bst.GaleAxe],
            _ when state.Statuses.Contains(Bst.Moonstalker) => [Bst.AvalancheAxe, Bst.SpinningAxe],
            _ => [],
        };

        foreach (var axe in pairing)
        {
            if (Usable(state, axe))
            {
                why.Add("axe that completes the combo");
                return axe;
            }
        }

        if (state.PlayerTp < options.SpendTpAt)
            return 0;

        foreach (var axe in new[] { Bst.GaleAxe, Bst.SpinningAxe, Bst.MistralAxe, Bst.AvalancheAxe })
        {
            if (Usable(state, axe))
            {
                why.Add($"axe at {state.PlayerTp} TP");
                return axe;
            }
        }

        return 0;
    }

    private static uint Heart(BstState state)
    {
        foreach (var heart in new[] { Bst.VolantHeart, Bst.RampantHeart, Bst.DurantHeart, Bst.EldritchHeart })
        {
            if (state.Statuses.Contains(heart))
                return heart;
        }

        return 0;
    }

    private static bool HasKinship(BstState state) => Bst.Kinships.Overlaps(state.Statuses);

    private static bool Has(BstState state, uint action) =>
        !Bst.Levels.TryGetValue(action, out var level) || state.Level >= level;

    private static bool Usable(BstState state, uint action) => Has(state, action) && state.Ready(action);
}

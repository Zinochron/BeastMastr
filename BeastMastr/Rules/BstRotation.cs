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

    /// <summary>Duty Action I: you take the target's enmity, and the familiar's cover ends.</summary>
    public const uint Challenge = 46750;

    /// <summary>Duty Action II: the familiar takes the target's enmity and every hit meant for you, for 45 seconds.</summary>
    public const uint Snarl = 46751;

    /// <summary>On you while a familiar's Snarl covers you.</summary>
    public const uint Covered = 2413;

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

    /// <summary>
    /// The opener's first summon, as the player wants it: the second or third familiar, to borrow from,
    /// so that the first familiar comes in second and starts the fight with its Release.
    /// </summary>
    public static readonly uint[] OpenerBattlehorns = [SecondBattlehorn, ThirdBattlehorn, FirstBattlehorn];

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
/// <param name="PartingBlowHornWithin">
/// Parting Blow only while another Battlehorn is ready within this many seconds — so the last familiar
/// is not sent off with nothing to follow it.
/// </param>
/// <param name="PartingBlowFinisherShare">…or when the target is down to this share of its HP, where the blow finishes it.</param>
/// <param name="DutyActions">Use Challenge and Snarl to decide who takes the hits.</param>
/// <param name="Tank">Who takes the hits when nobody is in trouble.</param>
/// <param name="PlayerLowShare">At or below this share of your HP, the familiar takes over with Snarl.</param>
/// <param name="FamiliarLowShare">At or below this share of the familiar's HP, you take over with Challenge.</param>
/// <param name="ReleaseWaitSeconds">
/// How long a familiar that has not used its Tempered Release is kept before Parting Blow may send it
/// off anyway. Past this the release is not coming — no Kinship, no Beast Mode — and the next familiar
/// is worth more than the wait.
/// </param>
public sealed record BstOptions(bool Combo, bool Resources, int SpendTpAt, bool UseBattlehorns, bool UsePartingBlow,
                                bool UseShieldCharge, float PartingBlowHornWithin = 10f,
                                float PartingBlowFinisherShare = 0.1f, bool DutyActions = true,
                                DutyTank Tank = DutyTank.Auto, float PlayerLowShare = 0.5f,
                                float FamiliarLowShare = 0.35f, float ReleaseWaitSeconds = 45f)
{
    public static BstOptions Default => new(true, true, 200, true, true, true);
}

/// <summary>Who takes the hits while neither you nor the familiar is low.</summary>
public enum DutyTank
{
    /// <summary>Whoever is being hit, until they run low; hits that cannot be dodged go to the familiar.</summary>
    Auto,

    /// <summary>The familiar, by Snarl, whenever it has the HP for it.</summary>
    Familiar,

    /// <summary>You, by Challenge, whenever the target turns to a familiar.</summary>
    Player,
}

/// <summary>Everything the rotation looks at, taken once per decision.</summary>
/// <param name="Ready">Whether an action can be used right now — the game's own answer, cooldown and all.</param>
/// <param name="TargetDistance">To the target's edge, in yalms; infinity without a target.</param>
/// <param name="InCombat">A fight is on — or the run has started one, which is when a familiar is summoned before the pull.</param>
/// <param name="PrePull">The run has started the fight but nothing is fighting yet: the time for the opener.</param>
/// <param name="HornsThisFight">How many Battlehorns this fight has seen.</param>
/// <param name="OtherHornReadyIn">
/// Seconds until a Battlehorn other than the summoned one can be used: 0 when one can now, infinity
/// when none will.
/// </param>
/// <param name="TargetHpShare">The target's share of HP left, 1 when not known.</param>
/// <param name="PlayerHpShare">Your share of HP left.</param>
/// <param name="FamiliarHpShare">The summoned familiar's share of HP left, 1 without one.</param>
/// <param name="TargetOnFamiliar">The target is attacking one of your familiars.</param>
/// <param name="UnavoidableHit">
/// A hit on its way that no position avoids — one aimed at you, or one that fills the arena — by name,
/// or null.
/// </param>
/// <param name="MayDash">
/// Shield Charge may be used: nothing is on the ground or on its way. Against Borgny it carried the
/// player through the Poison Clouds and the run died of it.
/// </param>
/// <param name="KeepLastPartingBlow">
/// Parting Blow is only for finishing the target, not for making room for the next familiar. Borgny with
/// the third familiar out (the user): its blow is not spent on Borgny unless it finishes it before the adds.
/// </param>
/// <param name="FamiliarLeaving">
/// Parting Blow has sent the familiar off and no new one has come yet. It stays on the field for a few
/// seconds, but the next Battlehorn is already usable, and that is when it was pressed in the recording.
/// </param>
/// <param name="FamiliarReleased">
/// The familiar out now has had its Tempered Release. Until it has, Parting Blow does not end its summon
/// to make room for the next one: the release is worth several hundred potency.
/// </param>
/// <param name="FamiliarOutFor">Seconds since the familiar out now arrived; infinity when not known.</param>
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
    bool FamiliarOut,
    bool FamiliarLeaving = false,
    bool PrePull = false,
    int HornsThisFight = 0,
    float OtherHornReadyIn = 0f,
    float TargetHpShare = 1f,
    float PlayerHpShare = 1f,
    float FamiliarHpShare = 1f,
    bool TargetOnFamiliar = false,
    string? UnavoidableHit = null,
    bool MayDash = true,
    bool KeepLastPartingBlow = false,
    bool FamiliarReleased = true,
    float FamiliarOutFor = float.PositiveInfinity);

/// <param name="Gcd">The weaponskill to press, or 0.</param>
/// <param name="Ogcd">The ability to press alongside it, or 0.</param>
/// <param name="Engage">
/// Whether to go for the target. False while the opener is still being set up before the pull —
/// walking in or attacking would start the fight before the familiars are ready.
/// </param>
public sealed record BstDecision(uint Gcd, uint Ogcd, string Why, bool Engage = true);

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

    /// <summary>
    /// The opener, as the job is played: a Battlehorn, Borrow from that familiar, then a second
    /// Battlehorn. The second summon grants One with Nature, so the first familiar's Tempered Release and
    /// the borrowed Beast Mode are both there when the fight starts.
    /// </summary>
    public static BstDecision Next(BstState state, BstOptions options)
    {
        var why = new List<string>();
        var ogcd = options.Resources ? Ability(state, options, why) : 0u;
        var isOpener = Array.IndexOf(Bst.Battlehorns, ogcd) >= 0 || ogcd == Bst.Borrow;

        // Before the pull nothing but the opener goes out, and nothing is engaged until it is done: at
        // Borgny, Shield Charge followed the first horn and started the fight with one familiar.
        var opening = state.PrePull && (isOpener || !OpenerDone(state, options));
        if (opening)
        {
            if (!isOpener)
                ogcd = 0;

            why.Add("opener before the pull");
        }

        var gcd = !opening && options.Combo && state.HasTarget && state.TargetDistance <= MeleeRange
                      ? Combo(state, why)
                      : 0u;

        return new BstDecision(gcd, ogcd, string.Join("; ", why), !opening);
    }

    /// <summary>Two familiars summoned — one below the second Battlehorn's level — or no horns to use.</summary>
    private static bool OpenerDone(BstState state, BstOptions options)
    {
        if (!options.UseBattlehorns || !options.Resources)
            return true;

        var wanted = state.Level >= Bst.Levels[Bst.SecondBattlehorn] ? 2 : 1;
        if (state.HornsThisFight >= wanted)
            return true;

        // Nothing left that could still be summoned: the opener cannot finish, so it does not hold the pull.
        foreach (var horn in Bst.Battlehorns)
        {
            if (Usable(state, horn))
                return false;
        }

        return !state.FamiliarOut || state.HornsThisFight > 0;
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

        // A familiar in the fight is what every other ability here works through. The recording
        // summoned before the pull and again straight after every Parting Blow.
        var familiar = state.FamiliarOut && !state.FamiliarLeaving;
        if (options.UseBattlehorns && !familiar)
        {
            var order = state.PrePull && state.HornsThisFight == 0 ? Bst.OpenerBattlehorns : Bst.Battlehorns;
            foreach (var horn in order)
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

        var duty = Duty(state, options, familiar, why);
        if (duty != 0)
            return duty;

        if (state.Statuses.Contains(Bst.OneWithNature) && Usable(state, Bst.TemperedRelease))
        {
            why.Add("One with Nature: Tempered Release");
            return Bst.TemperedRelease;
        }

        if (familiar && Usable(state, Bst.Borrow))
        {
            why.Add("Borrow");
            return Bst.Borrow;
        }

        // The opener's second summon: after Borrow has taken from the first familiar — or straight
        // away below the level Borrow is learned at.
        if (options.UseBattlehorns && familiar && state.HornsThisFight == 1 &&
            (HasKinship(state) || state.Level < Bst.Levels[Bst.Borrow]))
        {
            foreach (var horn in Bst.Battlehorns)
            {
                if (Usable(state, horn))
                {
                    why.Add("second familiar of the opener");
                    return horn;
                }
            }
        }

        var axe = Axe(state, options, why);
        if (axe != 0)
            return axe;

        var heart = Heart(state);
        if (heart == 0 && !state.Statuses.Contains(Bst.WaveringHeart) && familiar &&
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

        if (familiar && state.FamiliarTp <= 150 && Usable(state, Bst.RallyingCheer))
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

        if (options.UsePartingBlow && familiar && state.Level >= 30 &&
            !Usable(state, Bst.TemperedRelease) && !Usable(state, Bst.Borrow) && Usable(state, Bst.PartingBlow))
        {
            // Only with a familiar to follow: another Battlehorn ready soon. The last familiar of a
            // cycle goes only when the blow finishes the target. And never before the familiar has had
            // its Tempered Release — several hundred potency, and Parting Blow ends the summon (the
            // user). After ReleaseWaitSeconds the release is not coming, and holding the familiar costs
            // more than it saves.
            if (state.OtherHornReadyIn <= options.PartingBlowHornWithin && !state.KeepLastPartingBlow &&
                (state.FamiliarReleased || state.FamiliarOutFor >= options.ReleaseWaitSeconds))
            {
                why.Add("Parting Blow, to summon the next familiar");
                return Bst.PartingBlow;
            }

            if (state.TargetHpShare <= options.PartingBlowFinisherShare)
            {
                why.Add("Parting Blow to finish the target");
                return Bst.PartingBlow;
            }
        }

        if (options.UseShieldCharge && state.MayDash && state.TargetDistance > MeleeRange + 1f &&
            state.TargetDistance <= 20f && Usable(state, Bst.ShieldCharge))
        {
            why.Add("Shield Charge to close in");
            return Bst.ShieldCharge;
        }

        return 0;
    }

    /// <summary>
    /// Who takes the hits, by the two duty actions — they share one recast. Snarl hands everything aimed
    /// at you to the familiar: for a hit no position avoids (in the recording it was pressed for exactly
    /// those, Deadly Thrust and Cold Caress), and when your HP runs low. Challenge takes it back when the
    /// familiar runs low. Nothing is pressed before the pull: both draw the target.
    /// </summary>
    private static uint Duty(BstState state, BstOptions options, bool familiar, List<string> why)
    {
        if (!options.DutyActions || state.PrePull)
            return 0;

        var covered = state.Statuses.Contains(Bst.Covered);
        var playerLow = state.PlayerHpShare <= options.PlayerLowShare;
        var familiarLow = state.FamiliarHpShare <= options.FamiliarLowShare;

        if (familiar && !covered && !familiarLow && Usable(state, Bst.Snarl))
        {
            if (state.UnavoidableHit is { } hit)
            {
                why.Add($"Snarl: the familiar takes {hit}");
                return Bst.Snarl;
            }

            if (playerLow)
            {
                why.Add("Snarl: your HP is low");
                return Bst.Snarl;
            }

            if (options.Tank == DutyTank.Familiar)
            {
                why.Add("Snarl: the familiar tanks");
                return Bst.Snarl;
            }
        }

        // While a hit that cannot be dodged is on its way, the familiar's cover stays.
        if (playerLow || (covered && state.UnavoidableHit != null) || !Usable(state, Bst.Challenge))
            return 0;

        if (familiarLow && (covered || state.TargetOnFamiliar))
        {
            why.Add("Challenge: the familiar is low");
            return Bst.Challenge;
        }

        if (options.Tank == DutyTank.Player && state.TargetOnFamiliar && !covered)
        {
            why.Add("Challenge: you tank");
            return Bst.Challenge;
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

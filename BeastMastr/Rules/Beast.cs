using System.Collections.Generic;
using System.Linq;

namespace BeastMastr.Rules;

/// <summary>Which of the three slots an action fills. The game groups them this way and names them.</summary>
public enum ActionSlot
{
    Trick,
    TemperedRelease,

    /// <summary>Shared by every beast of a classification, so it is the same action for all Beastkin.</summary>
    Borrow,
}

/// <summary>One of a beast's three actions.</summary>
/// <param name="Description">In the player's language. Never matched against — see <see cref="TraitClassifier"/>.</param>
public sealed record BeastAction(
    ActionSlot Slot,
    string Name,
    string Description,
    DamageType Damage,
    BeastTrait Traits);

/// <summary>
/// A capturable beast, as the plugin thinks about one. Plain data with no game types in it, so the
/// filtering below can be exercised without a client running.
/// </summary>
/// <param name="Number">The "No." the bestiary shows, which is also its <c>XBMPet</c> row.</param>
/// <param name="Statuses">Straight out of the game's own eleven flags. Not inferred.</param>
public sealed record Beast(
    uint Number,
    string Name,
    uint IconId,
    int Classification,
    string ClassificationName,
    string Habitat,
    IReadOnlyList<BeastAction> Actions,
    IReadOnlySet<BeastStatus> Statuses)
{
    /// <summary>Everything its actions do, merged. Traits are per action; a filter asks about the beast.</summary>
    public BeastTrait Traits { get; } =
        Actions.Aggregate(BeastTrait.None, (all, action) => all | action.Traits);

    /// <summary>Every damage type it can deal, in slot order and without repeats.</summary>
    public IReadOnlyList<DamageType> DamageTypes { get; } =
        Actions.Select(action => action.Damage)
               .Where(damage => damage != DamageType.None)
               .Distinct()
               .ToList();

    public bool Inflicts(BeastStatus status) => Statuses.Contains(status);

    public bool Has(BeastTrait trait) => (Traits & trait) != 0;
}

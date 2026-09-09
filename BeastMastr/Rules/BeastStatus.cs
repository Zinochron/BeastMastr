namespace BeastMastr.Rules;

/// <summary>
/// The eleven statuses a beast's actions can inflict, in the order <c>BNpcResist</c> uses — the
/// same order <c>XBMPet</c>'s eleven bool columns follow, so slot n is column 11 + n.
///
/// These names were not read out of the game; nothing names them. They were derived by taking every
/// beast that sets a given column and reading what its action descriptions have in common: the only
/// three beasts setting slot 2 are the three whose actions paralyse. Nine of the eleven are
/// confirmed that way. See <c>README-DEV.md</c> for the evidence and for the two that are not.
/// </summary>
public enum BeastStatus
{
    /// <summary>Confirmed: apkallu, antling and morbol, all of which slow.</summary>
    Slow = 0,

    /// <summary>Confirmed: ziz and cobra petrify; chimera deep freezes, which sits in the same slot.</summary>
    Petrification = 1,

    /// <summary>Confirmed: opo-opo, coeurl and morbol, all of which paralyse.</summary>
    Paralysis = 2,

    /// <summary>
    /// Inferred, not confirmed. The five beasts setting it — coblyn, dullahan, golem, spriggan and
    /// ice golem — inflict nothing in either of their two visible action descriptions, which is
    /// itself evidence that the third action is missing from the sheet. Silence is what the slot
    /// would be in the usual eleven; check it against the notebook before relying on it.
    /// </summary>
    Silence = 3,

    /// <summary>Confirmed: dodo, worm and morbol blind.</summary>
    Blind = 4,

    /// <summary>Confirmed: diremite, wespe, flying trap and uragnite poison.</summary>
    Poison = 5,

    /// <summary>Confirmed: buffalo, the only beast that stuns.</summary>
    Stun = 6,

    /// <summary>Confirmed: lamb puts enemies to sleep; treant causes nightmares.</summary>
    Sleep = 7,

    /// <summary>Confirmed: diremite and slime bind.</summary>
    Bind = 8,

    /// <summary>
    /// Confirmed for mandragora and worm, which inflict heaviness. Goobbue and hydra sicken rather
    /// than slow, so the slot may be broader than "heavy" alone.
    /// </summary>
    Heavy = 9,

    /// <summary>Confirmed: ghost and rafflesia doom; Karlabos cuts HP to a single digit.</summary>
    Doom = 10,
}

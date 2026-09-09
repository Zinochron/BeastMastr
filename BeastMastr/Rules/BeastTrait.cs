using System;

namespace BeastMastr.Rules;

/// <summary>
/// What a beast's actions do beyond inflicting a status.
///
/// The eleven statuses are not in here: they are flags in the game's own data
/// (<see cref="BeastStatus"/>) and need no interpreting. These are the things that only exist as
/// prose in an action's description, which is why they are a separate, less certain layer.
/// Interrupt is deliberately absent — it looked like it belonged here and turned out to be
/// <see cref="BeastStatus.Interruption"/>, a plain bool.
/// </summary>
[Flags]
public enum BeastTrait
{
    None = 0,

    /// <summary>Removes a status ailment from allies. One of the two things genuinely not in the data.</summary>
    Cleanse = 1 << 0,

    /// <summary>Dispels a beneficial status from enemies. The other one.</summary>
    Dispel = 1 << 1,

    /// <summary>Generates or absorbs HP.</summary>
    Heal = 1 << 2,

    /// <summary>Raises allied damage, or grants them an offensive buff.</summary>
    BuffAllies = 1 << 3,

    /// <summary>Improves an allied resistance.</summary>
    ResistanceBuff = 1 << 4,

    /// <summary>Reduces allied vulnerability to physical or magic damage.</summary>
    VulnerabilityDown = 1 << 5,

    /// <summary>Lowers an enemy resistance.</summary>
    ResistanceDown = 1 << 6,

    /// <summary>Raises enemy vulnerability.</summary>
    VulnerabilityUp = 1 << 7,

    /// <summary>Reduces enemy accuracy.</summary>
    AccuracyDown = 1 << 8,

    Knockback = 1 << 9,

    /// <summary>Draws an enemy in.</summary>
    DrawIn = 1 << 10,

    /// <summary>Rushes to the enemy before striking.</summary>
    Rush = 1 << 11,

    /// <summary>Hastens self or allies.</summary>
    Haste = 1 << 12,

    /// <summary>Strikes several times in one action.</summary>
    MultiHit = 1 << 13,

    /// <summary>The familiar leaves after using it.</summary>
    Retreat = 1 << 14,
}

namespace BeastMastr.Rules;

/// <summary>
/// The nine damage types the game names in <c>XBMElement</c>, plus unaspected, which actions talk
/// about but the sheet does not list.
///
/// The order is <c>XBMElement</c>'s own row order, so a value casts straight to a row id.
/// </summary>
public enum DamageType
{
    None = 0,
    Fire = 1,
    Wind = 2,
    Earth = 3,
    Lightning = 4,
    Ice = 5,
    Water = 6,
    Blunt = 7,
    Piercing = 8,
    Slashing = 9,

    /// <summary>Not an <c>XBMElement</c> row; actions describe it, nothing indexes it.</summary>
    Unaspected = 100,
}

public static class DamageTypes
{
    /// <summary>Physical types answer to Phys. Resistance, the rest to Mag. Resistance.</summary>
    public static bool IsPhysical(this DamageType type) =>
        type is DamageType.Blunt or DamageType.Piercing or DamageType.Slashing;
}

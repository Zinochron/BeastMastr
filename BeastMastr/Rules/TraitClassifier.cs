using System;
using System.Collections.Generic;

namespace BeastMastr.Rules;

/// <summary>
/// Reads an action's description and works out its damage type and what else it does.
///
/// **The text handed in must be English**, whatever language the client runs in. The descriptions
/// are the only place cleanse and dispel exist at all, and matching prose in eleven languages would
/// be eleven ways to be wrong — so the catalogue reads the sheet twice, English to classify and the
/// player's language to display. Nothing derived here is ever shown; it only drives filters.
///
/// The corpus is small and extremely regular — fifty beasts, a hundred descriptions, built from a
/// handful of sentence frames — which is what makes plain substring matching honest here rather
/// than a guess.
/// </summary>
public static class TraitClassifier
{
    private static readonly (string Needle, DamageType Type)[] DamageNeedles =
    [
        ("slashing", DamageType.Slashing),
        ("piercing", DamageType.Piercing),
        ("blunt", DamageType.Blunt),
        ("fire-aspected", DamageType.Fire),
        ("wind-aspected", DamageType.Wind),
        ("earth-aspected", DamageType.Earth),
        ("lightning-aspected", DamageType.Lightning),
        ("ice-aspected", DamageType.Ice),
        ("water-aspected", DamageType.Water),
        ("unaspected", DamageType.Unaspected),
    ];

    private static readonly (string Needle, BeastTrait Trait)[] TraitNeedles =
    [
        ("removes a status ailment", BeastTrait.Cleanse),
        ("dispels", BeastTrait.Dispel),
        ("generates beastmaster hp", BeastTrait.Heal),
        ("hp absorption", BeastTrait.Heal),
        ("damage dealt by allies", BeastTrait.BuffAllies),
        ("keen edge", BeastTrait.BuffAllies),
        ("resistance of allies", BeastTrait.ResistanceBuff),
        ("vulnerability and improves", BeastTrait.VulnerabilityDown),
        ("resistance of enemies", BeastTrait.ResistanceDown),
        ("vulnerability of enemies", BeastTrait.VulnerabilityUp),
        ("accuracy", BeastTrait.AccuracyDown),
        ("knocks back", BeastTrait.Knockback),
        ("draws in", BeastTrait.DrawIn),
        ("rushes", BeastTrait.Rush),
        ("hastens", BeastTrait.Haste),
        ("retreats", BeastTrait.Retreat),
    ];

    /// <summary>
    /// "sixfold", "sevenfold" and so on. Listed rather than matched on the "-fold" suffix, because
    /// nothing guarantees a future description does not use the word some other way.
    /// </summary>
    private static readonly string[] MultiHitNeedles =
        ["twofold", "threefold", "fourfold", "fivefold", "sixfold", "sevenfold", "eightfold"];

    public static DamageType DamageOf(string englishDescription)
    {
        var text = englishDescription.ToLowerInvariant();

        foreach (var (needle, type) in DamageNeedles)
        {
            if (text.Contains(needle, StringComparison.Ordinal))
                return type;
        }

        return DamageType.None;
    }

    public static BeastTrait TraitsOf(string englishDescription)
    {
        var text = englishDescription.ToLowerInvariant();
        var traits = BeastTrait.None;

        foreach (var (needle, trait) in TraitNeedles)
        {
            if (text.Contains(needle, StringComparison.Ordinal))
                traits |= trait;
        }

        foreach (var needle in MultiHitNeedles)
        {
            if (!text.Contains(needle, StringComparison.Ordinal))
                continue;

            traits |= BeastTrait.MultiHit;
            break;
        }

        return traits;
    }

    /// <summary>
    /// Every trait the classifier can produce, for a filter UI that wants to offer them all rather
    /// than only the ones some beast happens to have.
    /// </summary>
    public static IReadOnlyList<BeastTrait> All { get; } =
        (BeastTrait[])Enum.GetValues(typeof(BeastTrait));
}

using System;
using System.Collections.Generic;
using System.Linq;
using BeastMastr.Rules;
using Lumina.Excel;
using Language = Lumina.Data.Language;

namespace BeastMastr.Data;

/// <summary>
/// Builds the fifty beasts out of the sheets, once, on first use.
///
/// Every sheet is read raw by column index because none of these columns are named upstream — see
/// <see cref="XbmColumns"/> for where each index came from.
/// </summary>
public sealed class BeastCatalog
{
    private List<Beast>? beasts;

    public bool IsBuilt => beasts != null;

    public IReadOnlyList<Beast> Beasts => beasts ??= Build();

    /// <summary>
    /// Beasts keyed by icon id, which is how a bestiary tile is resolved: the window hands out an
    /// icon per grid slot and nothing else that identifies the beast.
    /// </summary>
    public IReadOnlyDictionary<uint, Beast> ByIcon =>
        byIcon ??= Beasts.ToDictionary(beast => beast.IconId);

    private Dictionary<uint, Beast>? byIcon;

    public void Invalidate()
    {
        beasts = null;
        byIcon = null;
    }

    private static ExcelSheet<RawRow>? Sheet(string name, Language? language = null)
    {
        try
        {
            return Services.Data.Excel.GetSheet<RawRow>(language, name);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, $"Sheet \"{name}\" could not be opened.");
            return null;
        }
    }

    private List<Beast> Build()
    {
        var result = new List<Beast>();

        var display = Sheet(XbmColumns.XbmPet.Sheet);
        var pets = Sheet(XbmColumns.Pet.Sheet);
        var actions = Sheet("Action");
        var addon = Sheet("Addon");

        if (display == null || pets == null || actions == null || addon == null)
            return result;

        // Classification runs off the English text; the player sees the display sheet. Matching
        // prose in every language would be every language's chance to be wrong.
        var english = Sheet(XbmColumns.XbmPet.Sheet, Language.English) ?? display;

        if (display.Columns.Count != XbmColumns.XbmPet.ColumnCount)
        {
            Services.Log.Warning(
                $"XBMPet has {display.Columns.Count} columns, expected {XbmColumns.XbmPet.ColumnCount}. " +
                "A patch has moved something; every column index below is suspect.");
        }

        foreach (var row in display)
        {
            if (row.RowId == 0)
                continue;

            var beast = BuildOne(row, english, pets, actions, addon);
            if (beast != null)
                result.Add(beast);
        }

        Services.Log.Information($"Beast catalogue built: {result.Count} beasts.");
        return result;
    }

    private static Beast? BuildOne(RawRow row, ExcelSheet<RawRow> english, ExcelSheet<RawRow> pets,
                                   ExcelSheet<RawRow> actions, ExcelSheet<RawRow> addon)
    {
        var petId = Read(row, XbmColumns.XbmPet.Pet);
        if (!pets.TryGetRow(petId, out var pet))
            return null;

        var classification = (int)Read(row, XbmColumns.XbmPet.KinClass);
        english.TryGetRow(row.RowId, out var englishRow);

        var slots = new (ActionSlot Slot, uint ActionId, int TextColumn)[]
        {
            (ActionSlot.Trick, Read(pet, XbmColumns.Pet.TrickAction), XbmColumns.XbmPet.TrickText),
            (ActionSlot.TemperedRelease, Read(pet, XbmColumns.Pet.TemperedReleaseAction), XbmColumns.XbmPet.TemperedReleaseText),
            (ActionSlot.Borrow, XbmColumns.BorrowActionId(classification), -1),
        };

        var built = new List<BeastAction>();
        foreach (var (slot, actionId, textColumn) in slots)
        {
            var name = actions.TryGetRow(actionId, out var action)
                           ? action.ReadStringColumn(0).ExtractText()
                           : string.Empty;

            // Borrow has no description in XBMPet: it belongs to the classification, not the beast.
            var shown = textColumn < 0 ? string.Empty : row.ReadStringColumn(textColumn).ExtractText();
            var forClassifying = textColumn < 0
                                     ? string.Empty
                                     : englishRow.ReadStringColumn(textColumn).ExtractText();

            built.Add(new BeastAction(slot, Capitalise(name), shown,
                                      TraitClassifier.DamageOf(forClassifying),
                                      TraitClassifier.TraitsOf(forClassifying)));
        }

        var statuses = new HashSet<BeastStatus>();
        for (var slot = 0; slot < XbmColumns.XbmPet.StatusCount; slot++)
        {
            if (row.ReadColumn(XbmColumns.XbmPet.FirstStatus + slot) is true)
                statuses.Add((BeastStatus)slot);
        }

        var classificationName = addon.TryGetRow(XbmColumns.ClassificationAddonRow(classification), out var label)
                                     ? label.ReadStringColumn(0).ExtractText()
                                     : $"Class {classification}";

        return new Beast(row.RowId,
                         Capitalise(pet.ReadStringColumn(XbmColumns.Pet.Name).ExtractText()),
                         Read(row, XbmColumns.XbmPet.Icon),
                         classification,
                         classificationName,
                         // Column 2 is a location id nothing has been found to resolve; see README-DEV.
                         string.Empty,
                         built,
                         statuses);
    }

    private static uint Read(RawRow row, int column)
    {
        try
        {
            return Convert.ToUInt32(row.ReadColumn(column));
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Sheets store names lowercase — "squirrel" — and the game capitalises on render.</summary>
    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}

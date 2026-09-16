using System.Collections.Generic;
using BeastMastr.Rules;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace BeastMastr.Data;

/// <summary>
/// The job's actions as the game data has them, checked once against what <see cref="Bst"/> assumes.
///
/// The rotation is written against fixed ids and levels because it has to be testable without the
/// game. This is where those are held against the real sheet: a patch that moves a level or reorders
/// the combo is reported in the log and in the Run tab instead of quietly pressing the wrong thing.
/// </summary>
public sealed class BeastmasterJob
{
    /// <param name="TargetsEnemy">Used on the target rather than on the player.</param>
    /// <param name="Range">In yalms; -1 is melee.</param>
    public sealed record ActionInfo(uint Id, string Name, int Level, int Range, bool TargetsEnemy, uint ComboFrom);

    private readonly Dictionary<uint, ActionInfo> actions = [];

    public BeastmasterJob()
    {
        var sheet = Services.Data.GetExcelSheet<LuminaAction>();

        foreach (var (id, level) in Bst.Levels)
        {
            if (sheet.GetRowOrDefault(id) is not { } row)
            {
                Problems.Add($"Action {id} is missing from the game data.");
                continue;
            }

            var info = new ActionInfo(id, row.Name.ExtractText(), row.ClassJobLevel, row.Range, row.CanTargetHostile,
                                      row.ActionCombo.RowId);
            actions[id] = info;

            if (info.Level != level)
                Problems.Add($"{info.Name} is level {info.Level} in the game data, the rotation assumes {level}.");
        }

        Check(Bst.AxebladeBite, Bst.SmashAxe);
        Check(Bst.Shieldsplitter, Bst.AxebladeBite);

        foreach (var problem in Problems)
            Services.Log.Warning($"Beastmaster data: {problem}");
    }

    /// <summary>Where the game data disagrees with the rotation. Empty when all is as assumed.</summary>
    public List<string> Problems { get; } = [];

    public ActionInfo? Info(uint id) => actions.GetValueOrDefault(id);

    public string Name(uint id) => Lookup(id)?.Name ?? id.ToString();

    /// <summary>
    /// Whether an action is used on the target. Asked of adjusted ids too — Tempered Release turns from
    /// a self action into one aimed at an enemy — so anything not checked at load is looked up.
    /// </summary>
    public bool TargetsEnemy(uint id) => Lookup(id)?.TargetsEnemy ?? false;

    private ActionInfo? Lookup(uint id)
    {
        if (actions.TryGetValue(id, out var known))
            return known;

        var row = Services.Data.GetExcelSheet<LuminaAction>().GetRowOrDefault(id);
        var info = row is { } r
                       ? new ActionInfo(id, r.Name.ExtractText(), r.ClassJobLevel, r.Range, r.CanTargetHostile,
                                        r.ActionCombo.RowId)
                       : null;

        if (info != null)
            actions[id] = info;

        return info;
    }

    private void Check(uint action, uint comboFrom)
    {
        if (actions.TryGetValue(action, out var info) && info.ComboFrom != comboFrom)
            Problems.Add($"{info.Name} follows {info.ComboFrom} in the game data, the rotation assumes {comboFrom}.");
    }
}

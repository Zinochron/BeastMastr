using System.Collections.Generic;
using System.Linq;
using BeastMastr.Data;
using BeastMastr.Ipc;
using Dalamud.Game.ClientState.Conditions;

namespace BeastMastr.Automation.Run;

/// <summary>
/// Whether a run may start, whether it has to stop, and whether it should simply wait a moment —
/// the way Sortr's guard answers the same three questions for retainers.
/// </summary>
public static class RunSafety
{
    /// <summary>
    /// Plugins that act on their own in ways a run can collide with: answering dialogs, pressing
    /// actions, moving the character. Their being loaded is not a reason to refuse, only to warn.
    /// </summary>
    private static readonly string[] Colliding =
        ["WrathCombo", "YesAlready", "TextAdvance", "AutoDuty", "Questionable", "PandorasBox"];

    public static IReadOnlyList<string> LoadedCollisions() => Colliding.Where(PluginPresence.IsLoaded).ToList();

    /// <summary>Why a run cannot start here and now, or null.</summary>
    public static string? CannotStart(BoardModel board)
    {
        if (Services.Objects.LocalPlayer == null)
            return "There is no character.";

        if (!GaugeReader.IsBeastmaster)
            return "Switch to Beastmaster first.";

        if (!board.InRunZone)
            return "Start a board, or stand at the Crucible entrance in Central Shroud — a run begins on a " +
                   "board's start platform or at Lauda.";

        if (!NavmeshIpc.IsLoaded)
            return "vnavmesh is not loaded.";

        if (board.Graph is not { IsValid: true })
            return "The board is not known: " + board.Status;

        return null;
    }

    /// <summary>Why a run cannot start at the entrance and now, or null.</summary>
    public static string? CannotStartAtEntrance(Configuration configuration)
    {
        if (Services.Objects.LocalPlayer == null)
            return "There is no character.";

        if (!GaugeReader.IsBeastmaster)
            return "Switch to Beastmaster first.";

        if (!NavmeshIpc.IsLoaded)
            return "vnavmesh is not loaded.";

        if (configuration.LastBoardRowId == 0)
            return "No board is known yet. Open a board's window once (talk to Lauda), then press Run again.";

        return null;
    }

    /// <summary>Why a run under way has to end, or null.</summary>
    /// <param name="mayLeaveZone">After the boss, leaving the zone is how a run ends rather than a failure.</param>
    public static string? MustStop(BoardModel board, bool mayLeaveZone)
    {
        if (!mayLeaveZone && !Waiting() && !board.InRunZone)
            return "The run's zone was left.";

        if (Services.Objects.LocalPlayer != null && !GaugeReader.IsBeastmaster)
            return "The job changed.";

        return null;
    }

    /// <summary>A moment in which nothing should be done: loading, or a cutscene.</summary>
    public static bool Waiting() =>
        Services.Condition[ConditionFlag.BetweenAreas] || Services.Condition[ConditionFlag.BetweenAreas51] ||
        Services.Condition[ConditionFlag.WatchingCutscene] || Services.Condition[ConditionFlag.WatchingCutscene78] ||
        Services.Condition[ConditionFlag.OccupiedInCutSceneEvent];
}

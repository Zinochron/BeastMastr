using BeastMastr.Data;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Automation;

/// <summary>
/// The team list's own row click, shared by everything that picks rows in it — calling a fight's
/// familiars and picking the most hurt at a shop or campsite. One copy, because the types in it were
/// hard won and a second copy is where they would drift.
/// </summary>
public static unsafe class TeamListCommands
{
    /// <summary>Command the window's own click sends. Recorded, not guessed.</summary>
    private const int ToggleCommand = 1;

    /// <summary>
    /// Sends what clicking that row sends. A real click on the first familiar fires
    /// <c>FireCallback</c> with <c>[1, 0]</c> — command then row — and selecting and deselecting
    /// fire exactly the same thing, so it is a toggle rather than a set.
    ///
    /// **The types matter.** The recording reads <c>[0] Int=1 [1] UInt=0</c>: the command is an Int
    /// and the row is a UInt. Sending the row as an Int too was silently ignored — nothing errored,
    /// the window simply never took the familiar, which is what the read-back kept reporting.
    /// </summary>
    public static bool ToggleRow(int row)
    {
        if (row < 0 || !AddonReader.TryGet(XbmColumns.PetParty.Addon, out var addon))
            return false;

        var values = stackalloc AtkValue[2];
        values[0].SetInt(ToggleCommand);
        values[1].SetUInt((uint)row);

        addon->FireCallback(2, values);
        return true;
    }
}

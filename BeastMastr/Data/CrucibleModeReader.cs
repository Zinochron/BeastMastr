using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Data;

/// <summary>
/// The Crucible mode the board window is set to — Standard, First Degree, Second Degree, Third
/// Degree. The choice appears once every board has been cleared, and **the game does not remember
/// it**: every visit starts at Standard again.
///
/// It is a drop-down with two stepper buttons beside it. What is read here is a **position in its
/// own list**, not the name shown: the names are localised, a saved name would stop matching if the
/// client language changed, and a position does not.
/// </summary>
public static unsafe class CrucibleModeReader
{
    /// <param name="Index">Position among the modes, or -1 when the shown name is not one of them.</param>
    /// <param name="Count">
    /// How many modes there are, from the list itself. **Not** <c>Options.Count</c>: the list only
    /// draws rows it has needed, so freshly opened it carries one name — the one it is set to — and a
    /// count taken from it read "1 of 1" and made every higher mode look out of range.
    /// </param>
    /// <param name="Options">The names it has drawn so far. For reading and for the log, not for counting.</param>
    public sealed record Mode(int Index, int Count, string Label, IReadOnlyList<string> Options)
    {
        /// <summary>The list's own idea of what is selected. Kept for the log; the label decides.</summary>
        public int SelectedItemIndex { get; init; } = -1;
    }

    /// <summary>
    /// The four mode names live in the <c>Addon</c> sheet from 17870 on — Standard, First, Second and
    /// Third Degree — so they can be named without the board window being open, and in the client's own
    /// language.
    /// </summary>
    private const uint FirstModeAddonRow = 17870;

    public const int ModeCount = 4;

    private static string[]? names;

    /// <summary>The modes in their order, named as the game names them.</summary>
    public static IReadOnlyList<string> Names()
    {
        if (names != null)
            return names;

        var found = new string[ModeCount];
        var sheet = Services.Data.GetExcelSheet<Lumina.Excel.Sheets.Addon>();
        for (var i = 0; i < ModeCount; i++)
        {
            var text = sheet.GetRowOrDefault(FirstModeAddonRow + (uint)i)?.Text.ExtractText() ?? string.Empty;
            found[i] = text.Length > 0 ? text : $"Mode {i + 1}";
        }

        names = found;
        return names;
    }

    /// <summary>Text node of the drop-down's closed face, which shows the mode it is set to.</summary>
    private const uint FaceTextNodeId = 3;

    /// <summary>Text node inside one row of its list.</summary>
    private const uint RowTextNodeId = 2;

    /// <summary>
    /// What the board window is set to, or null when it is not open or does not offer the choice —
    /// which is the normal state until every board has been cleared.
    /// </summary>
    public static Mode? Read()
    {
        if (!AddonReader.TryGet(XbmColumns.StageDetailList.Addon, out var addon))
            return null;

        var dropDown = Inside(&addon->UldManager, ComponentType.DropDownList);
        if (dropDown == null)
            return null;

        var face = Inside(&dropDown->UldManager, ComponentType.CheckBox);
        var list = Inside(&dropDown->UldManager, ComponentType.List);
        if (face == null || list == null)
            return null;

        var label = TextOf(face, FaceTextNodeId);
        var options = new List<string>();
        var rows = (AtkComponentList*)list;

        for (var row = 0; row < rows->ListLength; row++)
        {
            var renderer = rows->GetItemRenderer(row);
            if (renderer == null)
                continue;

            // Rows are pooled, so a spare one carries no text. Those are not modes.
            var text = TextOf(&renderer->AtkComponentButton.AtkComponentBase, RowTextNodeId);
            if (text.Length > 0)
                options.Add(text);
        }

        // The label decides the position, because that is what proved out: stepping through the
        // modes, the name shown always landed at the right place among the ones drawn. The list's
        // own SelectedItemIndex is carried along unproven, for the log to compare against.
        return new Mode(options.IndexOf(label), rows->ListLength, label, options)
        {
            SelectedItemIndex = rows->SelectedItemIndex,
        };
    }

    /// <summary>
    /// The first component of a kind within one node list. Found by what it is rather than by an id:
    /// an id would have to be re-derived after any patch that moves the block, and there is exactly
    /// one drop-down in that window.
    /// </summary>
    private static AtkComponentBase* Inside(AtkUldManager* uld, ComponentType type)
    {
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null || (uint)node->Type < 1000)
                continue;

            var component = ((AtkComponentNode*)node)->Component;
            if (component != null && component->GetComponentType() == type)
                return component;
        }

        return null;
    }

    private static string TextOf(AtkComponentBase* component, uint nodeId)
    {
        var node = component->GetTextNodeById(nodeId);
        return node == null ? string.Empty : node->NodeText.ToString();
    }
}

using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;

namespace BeastMastr.UI;

public static class Widgets
{
    public static void Icon(uint iconId, float size = 20f)
    {
        var scaled = new Vector2(size * ImGuiHelpers.GlobalScale);

        if (iconId == 0)
        {
            ImGui.Dummy(scaled);
            return;
        }

        var texture = Services.Textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();
        if (texture == null)
            ImGui.Dummy(scaled);
        else
            ImGui.Image(texture.Handle, scaled);
    }

    public static void HelpMarker(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    /// <summary>
    /// A copy button that puts <paramref name="payload"/> on the clipboard. The data explorer's
    /// whole point is getting captures out of the game and into README-DEV.md, so every dump has one.
    /// </summary>
    public static void CopyButton(string label, string payload)
    {
        if (ImGui.SmallButton(label))
            ImGui.SetClipboardText(payload);
    }

    /// <summary>
    /// Writes a dump to a file and reports where it went. A node tree is hundreds of lines, which is
    /// past what a clipboard is comfortable for, and a file can simply be read by whoever is doing
    /// the mapping.
    /// </summary>
    public static void SaveButton(string label, string name, string payload, ref string lastPath)
    {
        if (ImGui.SmallButton(label))
            lastPath = Data.CaptureStore.Save(name, payload) ?? "could not be written — see the log";

        if (lastPath.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(lastPath);
        }
    }
}

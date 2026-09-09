using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BeastMastr.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace BeastMastr.UI;

/// <summary>
/// Puts what a Crucible room holds next to the room, instead of a click deep.
///
/// The board window carries no text whatsoever — it is icons and connecting lines — so every word
/// here comes from the room list beside it, paired by the index the board's own component hands out
/// per position. Drawn as an overlay rather than injected into the window: the same data will drive
/// native nodes later, and building it against a separate model first is what makes that swap cheap.
/// </summary>
public sealed class BoardOverlay : Window
{
    private readonly Configuration configuration;
    private readonly EnemyCache enemies;

    public BoardOverlay(Configuration configuration, EnemyCache enemies)
        : base("##BeastMastrBoard",
               ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground |
               ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
               ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoBringToFrontOnFocus)
    {
        this.configuration = configuration;
        this.enemies = enemies;
        IsOpen = true;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
    }

    public override bool DrawConditions() => configuration.ShowBoardOverlay;

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        Position = viewport.Pos;
        Size = viewport.Size;
        PositionCondition = ImGuiCond.Always;
        SizeCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        // The board window draws its own rooms with its own labels, and a second set of cards over
        // the top is just clutter in front of it. Cards are for the board you are standing on.
        //
        // It is still worth being here while that window is up, though: hovering a room is what
        // brings the enemy panel out, and this is the only place with a mouse position to attribute
        // it by.
        if (StageMapReader.IsOpen)
        {
            enemies.AttributeHover(ImGui.GetMousePos());
            return;
        }

        DrawWorldMarkers(StageDetailReader.Read(), enemies);
    }

    /// <summary>
    /// The board you are standing on. A run is walked across physical platforms with an icon
    /// floating over each, so this is where the cards belong — the board window is the thing you
    /// open to avoid needing them.
    /// </summary>
    private static void DrawWorldMarkers(IReadOnlyList<StageDetailReader.Room> details, EnemyCache enemies)
    {
        var markers = MapMarkerReader.ReadRooms();
        if (markers.Count == 0)
            return;

        var draw = ImGui.GetWindowDrawList();

        for (var i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            if (marker.World is not { } world)
                continue;

            if (!Services.GameGui.WorldToScreen(world, out var screen))
                continue;

            // The icon floats above the platform; the card goes under it rather than over it.
            var info = details.ElementAtOrDefault(i);
            var text = info == null ? marker.Kind?.ToString() ?? "?" : ShortLabel(info);
            var kind = info?.Kind ?? marker.Kind ?? XbmColumns.RoomKind.Enemy;

            var padding = new Vector2(5f, 2f) * ImGuiHelpers.GlobalScale;
            var size = ImGui.CalcTextSize(text);
            var topLeft = new Vector2(screen.X - ((size.X / 2f) + padding.X),
                                      screen.Y + (14f * ImGuiHelpers.GlobalScale));

            draw.AddRectFilled(topLeft, topLeft + size + (padding * 2f),
                               ImGui.ColorConvertFloat4ToU32(Colour(kind) with { W = 0.88f }), 3f);
            draw.AddText(topLeft + padding,
                         ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f)), text);

            // What is actually in there, once it has been hovered once — the weakness, the statuses
            // and whether it can be interrupted. Falls back to the game's own sentence until then.
            var known = info == null ? [] : enemies.InRoom(info.Index);
            var lines = known.Count > 0
                            ? known.Select(enemy => $"{enemy.Name}: {enemy.Summary}").ToList()
                            : info != null && info.Detail.Length > 0 ? [info.Detail] : new List<string>();

            var y = topLeft.Y + size.Y + (padding.Y * 2f);
            foreach (var line in lines)
            {
                draw.AddText(new Vector2(topLeft.X, y),
                             ImGui.ColorConvertFloat4ToU32(new Vector4(0.88f, 0.88f, 0.92f, 0.95f)), line);

                y += ImGui.GetTextLineHeight();
            }
        }
    }

    /// <summary>
    /// The game's own label without its "#2" suffix — short enough for a chip, and still in the
    /// player's language because it came from the window rather than from a table here.
    /// </summary>
    private static string ShortLabel(StageDetailReader.Room room)
    {
        var hash = room.Label.IndexOf('#');
        return (hash < 0 ? room.Label : room.Label[..hash]).Trim();
    }

    private static Vector4 Colour(XbmColumns.RoomKind kind) => kind switch
    {
        XbmColumns.RoomKind.Enemy => new Vector4(0.85f, 0.35f, 0.30f, 1f),
        XbmColumns.RoomKind.EliteEnemy => new Vector4(0.95f, 0.30f, 0.45f, 1f),
        XbmColumns.RoomKind.Boss => new Vector4(0.75f, 0.40f, 0.95f, 1f),
        XbmColumns.RoomKind.Shop => new Vector4(0.95f, 0.75f, 0.25f, 1f),
        XbmColumns.RoomKind.Campsite => new Vector4(0.40f, 0.80f, 0.45f, 1f),
        XbmColumns.RoomKind.Treasure => new Vector4(0.40f, 0.70f, 0.95f, 1f),
        XbmColumns.RoomKind.RandomEnemyOrTreasure => new Vector4(0.70f, 0.65f, 0.80f, 1f),
        _ => new Vector4(0.80f, 0.80f, 0.80f, 1f),
    };
}

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

    private static readonly Vector4 CurrentRoomOutline = new(1f, 0.85f, 0.3f, 1f);

    public BoardOverlay(Configuration configuration)
        : base("##BeastMastrBoard",
               ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground |
               ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
               ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoBringToFrontOnFocus)
    {
        this.configuration = configuration;
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
        var details = StageDetailReader.Read();

        // The board window, when it is open. Its tiles carry the index that pairs them with a room.
        var tiles = StageMapReader.Read();
        if (tiles.Count > 0)
        {
            var draw = ImGui.GetWindowDrawList();

            foreach (var tile in tiles)
                DrawChip(draw, tile, details.ElementAtOrDefault(tile.DetailIndex));

            DrawList(tiles, details);
            return;
        }

        DrawWorldMarkers(details);
    }

    /// <summary>
    /// The board you are standing on. A run is walked across physical platforms with an icon
    /// floating over each, so this is where the cards actually belong — the board window is the
    /// thing you open to avoid needing them.
    /// </summary>
    private static void DrawWorldMarkers(IReadOnlyList<StageDetailReader.Room> details)
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

            if (info != null && info.Detail.Length > 0)
            {
                draw.AddText(new Vector2(topLeft.X, topLeft.Y + size.Y + (padding.Y * 2f)),
                             ImGui.ColorConvertFloat4ToU32(new Vector4(0.88f, 0.88f, 0.92f, 0.95f)),
                             info.Detail);
            }
        }
    }

    /// <summary>
    /// A chip beside the tile. The board is three columns sixty pixels apart, so a chip wide enough
    /// for a sentence would cover its neighbours — this carries the kind and nothing else, and the
    /// panel beside the board carries the sentences.
    /// </summary>
    private static void DrawChip(ImDrawListPtr draw, StageMapReader.BoardRoom room,
                                 StageDetailReader.Room? info)
    {
        if (room.IsCurrent)
        {
            draw.AddRect(room.ScreenPosition - new Vector2(2f),
                         room.ScreenPosition + room.Size + new Vector2(2f),
                         ImGui.ColorConvertFloat4ToU32(CurrentRoomOutline), 4f, ImDrawFlags.None, 2.5f);
        }

        if (info == null)
            return;

        var text = ShortLabel(info);
        var padding = new Vector2(4f, 1f) * ImGuiHelpers.GlobalScale;
        var textSize = ImGui.CalcTextSize(text);

        // Under the tile rather than beside it: the columns are tight, the rows are not.
        var topLeft = new Vector2(room.Centre.X - ((textSize.X / 2f) + padding.X),
                                  room.ScreenPosition.Y + room.Size.Y - (2f * ImGuiHelpers.GlobalScale));
        var bottomRight = topLeft + textSize + (padding * 2f);

        draw.AddRectFilled(topLeft, bottomRight, ImGui.ColorConvertFloat4ToU32(Colour(info.Kind) with { W = 0.85f }), 3f);
        draw.AddText(topLeft + padding, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f)), text);
    }

    /// <summary>
    /// The room list, in move order, beside the board. This is where the game's own sentence goes —
    /// the thing that currently costs a click per room to read.
    /// </summary>
    private void DrawList(IReadOnlyList<StageMapReader.BoardRoom> rooms,
                          IReadOnlyList<StageDetailReader.Room> details)
    {
        if (details.Count == 0)
            return;

        var anchor = rooms.Aggregate(new Vector2(float.MaxValue, float.MaxValue),
                                     (left, room) => Vector2.Min(left, room.ScreenPosition));

        var currentIndex = rooms.FirstOrDefault(room => room.IsCurrent)?.DetailIndex ?? -1;

        var draw = ImGui.GetWindowDrawList();
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        var width = 300f * ImGuiHelpers.GlobalScale;
        var origin = new Vector2(anchor.X - width - (16f * ImGuiHelpers.GlobalScale), anchor.Y);

        // Off the left edge on a narrow screen; put it to the right of the board instead.
        if (origin.X < 0f)
        {
            var rightmost = rooms.Max(room => room.ScreenPosition.X + room.Size.X);
            origin = new Vector2(rightmost + (16f * ImGuiHelpers.GlobalScale), anchor.Y);
        }

        var ordered = details.OrderBy(room => room.Move).ThenBy(room => room.Index).ToList();
        var height = (ordered.Count + 1) * lineHeight;

        draw.AddRectFilled(origin - new Vector2(8f), origin + new Vector2(width, height) + new Vector2(8f),
                           ImGui.ColorConvertFloat4ToU32(new Vector4(0.05f, 0.05f, 0.07f, 0.82f)), 6f);

        var y = origin.Y;
        foreach (var room in ordered)
        {
            var isCurrent = room.Index == currentIndex;
            var label = $"{room.Move,2}  {room.Label}";

            draw.AddText(new Vector2(origin.X, y),
                         ImGui.ColorConvertFloat4ToU32(isCurrent ? CurrentRoomOutline : Colour(room.Kind)),
                         label);

            draw.AddText(new Vector2(origin.X + (130f * ImGuiHelpers.GlobalScale), y),
                         ImGui.ColorConvertFloat4ToU32(new Vector4(0.82f, 0.82f, 0.85f, 1f)),
                         room.Detail);

            y += lineHeight;
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

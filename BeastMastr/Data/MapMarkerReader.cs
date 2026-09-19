using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace BeastMastr.Data;

/// <summary>
/// The map's markers.
///
/// The Crucible's rooms are physical platforms you walk across, with an icon floating over each one
/// that also shows on the minimap. Those icons are not objects — the object table holds nothing but
/// the player in there — and no window draws them, so the map's own marker list is what is left.
///
/// Coordinates arrive in map units and are converted here. The board's own markers are told apart
/// from the surrounding zone's landmarks by icon id, and those icons name the room's kind outright.
/// </summary>
public static unsafe class MapMarkerReader
{
    /// <summary>
    /// The Crucible's room icons, by icon id. Read off a full board against the minimap: the rows
    /// of markers matched the rows of icons one for one, which fixes every one of these.
    /// 63853 is the gap — no board seen so far has offered a Random Enemy or Treasure room.
    /// </summary>
    public static readonly IReadOnlyDictionary<uint, XbmColumns.RoomKind> RoomIcons =
        new Dictionary<uint, XbmColumns.RoomKind>
        {
            [63850] = XbmColumns.RoomKind.Campsite,
            [63851] = XbmColumns.RoomKind.Shop,
            [63852] = XbmColumns.RoomKind.Treasure,
            [63854] = XbmColumns.RoomKind.Enemy,
            [63855] = XbmColumns.RoomKind.EliteEnemy,
            [63856] = XbmColumns.RoomKind.Boss,
        };

    /// <summary>
    /// Is this one of the board's rooms, rather than a landmark of the zone around it? The whole icon
    /// range counts, not only the icons already identified: a room whose icon is not in the table yet
    /// still takes a place on the board, and leaving it out would shift every row after it.
    /// </summary>
    public static bool IsRoom(uint iconId) =>
        iconId >= XbmColumns.Crucible.FirstRoomIcon && iconId <= XbmColumns.Crucible.LastRoomIcon;

    /// <param name="Source">Which list it came from — the two behave differently and it matters which is which.</param>
    /// <param name="World">Where the icon floats, in world units, or null when the map offers no transform.</param>
    public sealed record Marker(string Source, uint IconId, int MapX, int MapY, string Subtext, Vector3? World)
    {
        public XbmColumns.RoomKind? Kind => RoomIcons.TryGetValue(IconId, out var kind) ? kind : null;
    }

    /// <summary>
    /// The board's rooms, in the order the room list describes them.
    ///
    /// Sorted by map Y ascending and then map X descending, which is not a guess: laid against a
    /// full twelve-room board, that order reproduced the room list exactly — boss, shop, campsite,
    /// treasure, elite, … — and every kind the icons imply matched the kind the list gives. So a
    /// marker's position in this list is its index into the room list.
    /// </summary>
    public static List<Marker> ReadRooms()
    {
        var rooms = Read().FindAll(marker => IsRoom(marker.IconId));
        rooms.Sort((left, right) => left.MapY != right.MapY
                                        ? left.MapY.CompareTo(right.MapY)
                                        : right.MapX.CompareTo(left.MapX));
        return rooms;
    }

    /// <summary>
    /// What the map says about its own coordinate space. Printed rather than trusted: the first
    /// transform put every card roughly seven hundred units east of the board, which is what these
    /// numbers are needed to explain.
    /// </summary>
    public static string DescribeTransform()
    {
        var agent = AgentMap.Instance();
        if (agent == null)
            return "no map agent";

        var player = Services.Objects.LocalPlayer;
        var at = player == null ? "unknown" : $"{player.Position.X:0.00}/{player.Position.Y:0.00}/{player.Position.Z:0.00}";

        return $"mapId={agent->CurrentMapId} territory={agent->CurrentTerritoryId} " +
               $"sizeFactor={agent->CurrentMapSizeFactor} " +
               $"offsetX={agent->CurrentOffsetX} offsetY={agent->CurrentOffsetY} " +
               $"player={at}";
    }

    private static uint zoneMapTerritory;
    private static uint zoneMap;

    /// <summary>The map the TerritoryType sheet gives a zone.</summary>
    private static uint ZoneMap(uint territory)
    {
        if (territory != zoneMapTerritory)
        {
            zoneMapTerritory = territory;
            zoneMap = Services.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(territory)?.Map.RowId ?? 0;
        }

        return zoneMap;
    }

    public static List<Marker> Read()
    {
        var markers = new List<Marker>();
        var agent = AgentMap.Instance();

        if (agent == null)
            return markers;

        foreach (ref var info in agent->MapMarkers)
            Add(markers, "MapMarkers", ref info.MapMarker, agent);

        foreach (ref var temp in agent->TempMapMarkers)
            Add(markers, "TempMapMarkers", ref temp.MapMarker, agent);

        return markers;
    }

    private static void Add(List<Marker> into, string source, ref MapMarkerBase marker, AgentMap* agent)
    {
        // An empty slot is an icon of zero at the origin. The arrays are fixed size and mostly empty.
        if (marker.IconId == 0 && marker.X == 0 && marker.Y == 0)
            return;

        into.Add(new Marker(source, marker.IconId, marker.X, marker.Y,
                            marker.Subtext.HasValue ? marker.Subtext.ToString() : string.Empty,
                            ToWorld(marker, agent)));
    }

    /// <summary>
    /// Map units to world units: <c>raw / (16 × sizeFactor / 100) + offset</c>.
    ///
    /// Both halves were got wrong once and both were settled by measurement rather than argument.
    ///
    /// The sign first: the board's middle column is map X zero against an offset of -700, and the
    /// player walks that column at world X of about -700 — so the offset is added. Subtracting it
    /// put every card seven hundred units the other way.
    ///
    /// Then the scale. With a plain sixteen the middle of the board sat almost right and everything
    /// else spread too far out, which is what a scale error looks like and not an offset one. This
    /// map's size factor is 400, so the divisor is 64 — and standing in the boss room's trigger puts
    /// the player at Z -74.33 where that divisor places the boss marker at -75.00. A normal zone has
    /// a size factor of 100, which gives back the plain sixteen.
    ///
    /// The height is the one thing this cannot know: markers carry no Y at all. The player's own
    /// height is used, which is right for a board you walk across on the flat and wrong the moment
    /// one is not.
    /// </summary>
    private static Vector3? ToWorld(MapMarkerBase marker, AgentMap* agent)
    {
        // Only the zone's own map places the board. A fight's arena shows another map of the same zone,
        // and its offsets put every room around the arena instead — which made the run think it was
        // still on the board after Commence Battle, and the fight never counted as begun.
        if (agent->CurrentMapId == 0 || agent->CurrentMapId != ZoneMap(agent->CurrentTerritoryId))
            return null;

        var height = Services.Objects.LocalPlayer?.Position.Y ?? 0f;
        var scale = 16f * (agent->CurrentMapSizeFactor / 100f);

        if (scale <= 0f)
            return null;

        return new Vector3((marker.X / scale) + agent->CurrentOffsetX,
                           height,
                           (marker.Y / scale) + agent->CurrentOffsetY);
    }
}

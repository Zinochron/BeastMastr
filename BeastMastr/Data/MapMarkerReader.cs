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

    /// <summary>Is this one of the board's rooms, rather than a landmark of the zone around it?</summary>
    public static bool IsRoom(uint iconId) => RoomIcons.ContainsKey(iconId);

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
    /// Map units to world units: sixteen units to the yalm, **plus** the map's origin.
    ///
    /// The sign was wrong first time round, and the numbers say so plainly. The board's middle
    /// column is map X zero and the map's offset is -700, and the player walks that column at world
    /// X of about -705 — so the offset is added. Subtracting it put every card seven hundred units
    /// the other way, which is why they all appeared far off to one side.
    ///
    /// The height is the one thing this cannot know: markers carry no Y at all. The player's own
    /// height is used, which is right for a board you walk across on the flat and wrong the moment
    /// one is not.
    /// </summary>
    private static Vector3? ToWorld(MapMarkerBase marker, AgentMap* agent)
    {
        if (agent->CurrentMapId == 0)
            return null;

        var height = Services.Objects.LocalPlayer?.Position.Y ?? 0f;

        return new Vector3((marker.X / 16f) + agent->CurrentOffsetX,
                           height,
                           (marker.Y / 16f) + agent->CurrentOffsetY);
    }
}

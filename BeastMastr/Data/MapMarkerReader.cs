using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace BeastMastr.Data;

/// <summary>
/// The map's markers.
///
/// The Crucible's rooms are physical platforms you walk across, with an icon floating over each one
/// that also shows on the minimap. Those icons are not objects — the object table holds nothing but
/// the player in there — and no window draws them, so the map's own marker list is what is left.
///
/// Coordinates here are the map's, not the world's, and the two are not the same units. Nothing in
/// this file converts between them: the diagnostic prints the raw numbers next to the player's
/// known world position, which is how the relationship gets established rather than assumed.
/// </summary>
public static unsafe class MapMarkerReader
{
    /// <param name="Source">Which list it came from — the two behave differently and it matters which is which.</param>
    public sealed record Marker(string Source, uint IconId, int MapX, int MapY, string Subtext);

    public static List<Marker> Read()
    {
        var markers = new List<Marker>();
        var agent = AgentMap.Instance();

        if (agent == null)
            return markers;

        foreach (ref var info in agent->MapMarkers)
            Add(markers, "MapMarkers", ref info.MapMarker);

        foreach (ref var temp in agent->TempMapMarkers)
            Add(markers, "TempMapMarkers", ref temp.MapMarker);

        return markers;
    }

    private static void Add(List<Marker> into, string source, ref MapMarkerBase marker)
    {
        // An empty slot is an icon of zero at the origin. The arrays are fixed size and mostly empty.
        if (marker.IconId == 0 && marker.X == 0 && marker.Y == 0)
            return;

        into.Add(new Marker(source, marker.IconId, marker.X, marker.Y,
                            marker.Subtext.HasValue ? marker.Subtext.ToString() : string.Empty));
    }
}

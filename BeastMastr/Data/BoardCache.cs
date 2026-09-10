using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;

namespace BeastMastr.Data;

/// <summary>
/// The board being played, kept so it can be read where the board window is not.
///
/// The room list shows two different things depending on where you open it, and telling them apart
/// is the whole job here:
///
/// - **At the entrance, before a run**, it lists the whole board — twelve rooms across nine moves.
///   That is kept, and saved: a board is fixed, so it is worth having in the next session too.
/// - **Inside a run**, it lists only the room at your position, and the board marks that room as
///   current. Every capture taken in a run shows exactly that: one room, on the same move as the
///   marked tile. That is where the run is, and it is the room about to be entered — at the very
///   start of a run it is move 1, with nothing done yet.
///
/// So a reading whose rooms span several moves is a board, and one whose rooms all share a move is
/// a position. A branching move offers two rooms on the same move, which is still a position.
/// </summary>
public sealed class BoardCache : IDisposable
{
    /// <summary>Frames between looks. A board changes when a room is entered, not per frame.</summary>
    private const int Interval = 20;

    private readonly Configuration configuration;

    private List<StageDetailReader.Room> rooms = [];
    private int ticks;

    public BoardCache(Configuration configuration)
    {
        this.configuration = configuration;

        rooms = configuration.LastBoard.Rooms
                             .Select(room => new StageDetailReader.Room(room.Index, room.Move,
                                                                       (XbmColumns.RoomKind)room.Kind,
                                                                       room.Label, room.Detail))
                             .ToList();

        Source = rooms.Count > 0
                     ? $"remembered from an earlier session — {rooms.Count} rooms"
                     : "nothing read yet";

        Services.Framework.Update += OnUpdate;
    }

    public IReadOnlyList<StageDetailReader.Room> Rooms => rooms;

    /// <summary>
    /// The move of the room you are at: the one the board marks, which is the one about to be
    /// entered. -1 until a run has been looked at — the board only says while its windows are open.
    /// </summary>
    public int CurrentMove { get; private set; } = -1;

    /// <summary>
    /// The territory the run was last located in. The run's own HUD is the primary sign of being in
    /// one, but it has not been seen in a capture yet, so this is the second opinion.
    /// </summary>
    public uint RunTerritory { get; private set; }

    /// <summary>Where the rooms came from, for the Board tab to show.</summary>
    public string Source { get; private set; }

    /// <summary>Every move the board has rooms on, in order.</summary>
    public IReadOnlyList<int> Moves =>
        rooms.Select(room => room.Move).Distinct().OrderBy(move => move).ToList();

    /// <summary>The rooms a move offers — two of them where the board branches.</summary>
    public IReadOnlyList<StageDetailReader.Room> OnMove(int move) =>
        rooms.Where(room => room.Move == move).ToList();

    /// <summary>The next move with rooms on it, or -1 at the end of the board.</summary>
    public int After(int move)
    {
        var later = Moves.Where(candidate => candidate > move).ToList();
        return later.Count > 0 ? later[0] : -1;
    }

    /// <summary>
    /// The move to brief: the room you are at, which has not been entered yet. Before any run has
    /// been located, the board's first move — which is right at the start of one.
    /// </summary>
    public int NextMove => CurrentMove >= 0 && Moves.Contains(CurrentMove)
                               ? CurrentMove
                               : Moves.Count > 0 ? Moves[0] : -1;

    /// <summary>Throw the remembered board away, for when it is showing the wrong one.</summary>
    public void Forget()
    {
        rooms = [];
        CurrentMove = -1;
        Source = "forgotten";

        configuration.LastBoard = new SavedBoard();
        configuration.Save();
    }

    private void OnUpdate(IFramework framework)
    {
        if (--ticks > 0)
            return;

        ticks = Interval;

        if (!StageDetailReader.IsOpen)
            return;

        var read = StageDetailReader.Read();
        if (read.Count == 0)
            return;

        var marked = StageMapReader.Read().Any(tile => tile.IsCurrent);

        if (read.Select(room => room.Move).Distinct().Count() > 1)
        {
            Remember(read);

            // The whole board with nothing marked is the entrance, before a run: whatever move was
            // known belongs to a run that is over.
            if (!marked)
                CurrentMove = -1;

            return;
        }

        // Every room on one move: a position. It only counts while the board marks a room as
        // current, because the window stays loaded after it closes and keeps its last contents —
        // a single leftover room was captured that way in Central Shroud, nowhere near a run, and
        // there nothing was marked.
        if (!marked)
            return;

        if (CurrentMove != read[0].Move)
            Services.Log.Debug($"Run located on move {read[0].Move}: {read[0].Label}.");

        CurrentMove = read[0].Move;
        RunTerritory = Services.ClientState.TerritoryType;
    }

    private void Remember(List<StageDetailReader.Room> read)
    {
        Source = $"read from the board — {read.Count} rooms";

        if (rooms.SequenceEqual(read))
            return;

        rooms = read;
        configuration.LastBoard = new SavedBoard
        {
            Rooms = read.Select(room => new SavedRoom
                        {
                            Index = room.Index,
                            Move = room.Move,
                            Kind = (int)room.Kind,
                            Label = room.Label,
                            Detail = room.Detail,
                        })
                        .ToList(),
        };

        configuration.Save();
        Services.Log.Information($"Board remembered: {read.Count} rooms across {Moves.Count} moves.");
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}

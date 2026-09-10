using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;

namespace BeastMastr.Data;

/// <summary>
/// The board being played, kept so that it can be read where the board window is not.
///
/// The room list has to be opened before a run starts, which is the whole reason this works: the
/// data is guaranteed to pass through at least once, so nothing has to be fetched at the moment it
/// is wanted. And a board is fixed — the same place lays out the same rooms every time — so what
/// was read is worth keeping between sessions rather than only for the run.
///
/// It is rewritten every time the list is open. That makes a wrong entry self-correcting: the worst
/// a stale board can do is be shown until you next look at the real one.
/// </summary>
public sealed class BoardCache : IDisposable
{
    /// <summary>Frames between looks. A board changes when a room is entered, not per frame.</summary>
    private const int Interval = 20;

    /// <summary>
    /// A reading has to describe more than one room to count as a board.
    ///
    /// The window stays loaded after it is closed and keeps whatever it last held, and a leftover
    /// reading of a single room was captured that way — outside the Crucible entirely, in Central
    /// Shroud. One room is what that looks like; a real board has at least a first room and a boss.
    /// </summary>
    private const int LeastRoomsForABoard = 2;

    private readonly Configuration configuration;

    private List<StageDetailReader.Room> rooms = [];
    private uint territory;
    private int ticks;

    public BoardCache(Configuration configuration)
    {
        this.configuration = configuration;

        territory = Services.ClientState.TerritoryType;
        Load();

        Services.Framework.Update += OnUpdate;
    }

    public IReadOnlyList<StageDetailReader.Room> Rooms => rooms;

    /// <summary>
    /// The move the run is standing on: 0 before the first room, 1 once it is done. -1 while the
    /// board has not said — it only says while one of its windows is open.
    /// </summary>
    public int CurrentMove { get; private set; } = -1;

    /// <summary>Where the rooms came from, for the Board tab to show.</summary>
    public string Source { get; private set; } = "nothing read yet";

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

    /// <summary>The previous move with rooms on it, or -1 at the start.</summary>
    public int Before(int move)
    {
        var earlier = Moves.Where(candidate => candidate < move).ToList();
        return earlier.Count > 0 ? earlier[^1] : -1;
    }

    /// <summary>
    /// The move to brief. Where the run has been located that is the one after it; where it has not,
    /// it is the board's first — which is right at the start of a run and wrong nowhere that
    /// matters, since a briefing for a room already behind you is easy to recognise as such.
    /// </summary>
    public int NextMove => After(Math.Max(CurrentMove, 0));

    /// <summary>Throw the remembered board away, for when it is showing the wrong one.</summary>
    public void Forget()
    {
        rooms = [];
        CurrentMove = -1;
        Source = "forgotten";

        if (configuration.KnownBoards.Remove(territory))
            configuration.Save();
    }

    private void OnUpdate(IFramework framework)
    {
        if (--ticks > 0)
            return;

        ticks = Interval;

        var here = Services.ClientState.TerritoryType;
        if (here != territory)
        {
            // A different place is a different board and a run that has not started. Keeping the
            // old move would brief a room from somewhere else.
            territory = here;
            CurrentMove = -1;
            Load();
        }

        if (StageDetailReader.IsOpen)
        {
            var read = StageDetailReader.Read();
            if (read.Count >= LeastRoomsForABoard)
                Remember(read);
        }

        if (rooms.Count > 0 && StageMapReader.IsOpen)
        {
            var move = StageMapReader.MoveOf(RoomsPerMove());
            if (move >= 0)
                CurrentMove = move;
        }
    }

    /// <summary>How many rooms each move offers, move one first — what the board's rows are checked against.</summary>
    private List<int> RoomsPerMove() =>
        rooms.GroupBy(room => room.Move)
             .OrderBy(move => move.Key)
             .Select(move => move.Count())
             .ToList();

    private void Load()
    {
        rooms = configuration.KnownBoards.TryGetValue(territory, out var saved)
                    ? saved.Rooms
                           .Select(room => new StageDetailReader.Room(room.Index, room.Move,
                                                                     (XbmColumns.RoomKind)room.Kind,
                                                                     room.Label, room.Detail))
                           .ToList()
                    : [];

        Source = rooms.Count > 0
                     ? $"remembered from an earlier visit — {rooms.Count} rooms"
                     : "nothing read here yet";
    }

    private void Remember(List<StageDetailReader.Room> read)
    {
        Source = $"read from the board — {read.Count} rooms";

        if (rooms.SequenceEqual(read))
            return;

        rooms = read;
        configuration.KnownBoards[territory] = new SavedBoard
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
        Services.Log.Information($"Board in territory {territory} remembered: {read.Count} rooms " +
                                 $"across {Moves.Count} moves.");
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}

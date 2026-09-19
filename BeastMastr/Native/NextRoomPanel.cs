using System;
using System.Collections.Generic;
using System.Numerics;
using BeastMastr.Data;
using BeastMastr.Rules;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;

namespace BeastMastr.Native;

/// <summary>
/// One room, the next one, in a window of the game's own.
///
/// The board shows twelve rooms at once and every one of them is a click away from what it actually
/// holds. Only one of them is a decision you are about to make, so this shows that one and says
/// nothing about the rest — the arrows are there for looking further ahead when you want to, which
/// is a different question from what you need in front of you.
///
/// Its content comes from <see cref="BoardCache"/> rather than from a window, so it still works out
/// in the run, where both board windows are closed.
/// </summary>
public sealed class NextRoomPanel : IDisposable
{
    /// <summary>Frames between looks. Nothing here changes faster than a room is entered.</summary>
    private const int RecheckInterval = 20;

    private readonly Configuration configuration;
    private readonly BoardCache board;
    private readonly EnemyCache enemies;
    private readonly BoardModel model;
    private readonly RouteKeeper route;
    private readonly Action walkNext;
    private readonly Func<bool> nativeUiReady;

    private NextRoomAddon? window;

    /// <summary>How many moves past the next one the arrows have walked. Zero is the next room.</summary>
    private int lookAhead;

    private int briefedFor = int.MinValue;
    private string drawn = string.Empty;

    private int ticksUntilRecheck;

    /// <summary>
    /// Set once anything here throws, and never cleared. A node constructor that fails leaves a
    /// half-built object whose finalizer takes the game down, so one failure has to disable the
    /// feature rather than be retried every frame.
    /// </summary>
    private bool broken;

    public NextRoomPanel(Configuration configuration, BoardCache board, EnemyCache enemies, BoardModel model,
                         RouteKeeper route, Action walkNext, Func<bool> nativeUiReady)
    {
        this.walkNext = walkNext;
        this.configuration = configuration;
        this.board = board;
        this.enemies = enemies;
        this.model = model;
        this.route = route;
        this.nativeUiReady = nativeUiReady;

        Services.Framework.Update += OnUpdate;
    }

    /// <summary>
    /// Opened by hand with <c>/beastmastr room</c>, whatever the run is doing. The automatic rule is
    /// a guess about when you want it, and a window you cannot find is worse than one that shows up
    /// when asked.
    /// </summary>
    private bool forced;

    /// <summary>Why the window is or is not showing, in words. The first version kept that to itself.</summary>
    public string Status { get; private set; } = "Not checked yet.";

    public void Toggle()
    {
        forced = !forced;
        ticksUntilRecheck = 0;
    }

    /// <summary>
    /// Whether a run is under way: the run's own HUD is up, or this is the territory the board last
    /// marked a position in. Two signs, because the HUD has not been seen in a capture yet and the
    /// territory has — every capture taken in a run was in the same one.
    /// </summary>
    private bool InARun =>
        AddonReader.IsOpen(XbmColumns.ContentsMainHUD.Addon)
        || (board.RunTerritory != 0 && Services.ClientState.TerritoryType == board.RunTerritory);

    private void OnUpdate(IFramework framework)
    {
        if (broken || --ticksUntilRecheck > 0)
            return;

        ticksUntilRecheck = RecheckInterval;

        try
        {
            Refresh();
        }
        catch (Exception ex)
        {
            broken = true;
            Services.Log.Error(ex, "The next-room panel failed; leaving it closed for this session.");

            try
            {
                Close();
            }
            catch (Exception cleanup)
            {
                Services.Log.Error(cleanup, "Could not clean up after that either.");
            }
        }
    }

    private void Refresh()
    {
        // Entering a room answers the question the arrows were asking, so they go back to the next
        // room rather than staying however far ahead they were left.
        if (board.CurrentMove != briefedFor)
        {
            briefedFor = board.CurrentMove;
            lookAhead = 0;
        }

        var hidden = !nativeUiReady() ? "Waiting for the game's window toolkit to start."
                     : forced ? null
                     : !configuration.ShowNextRoom ? "Turned off in the settings."
                     : !InARun ? "Not in a run — it only opens on its own inside one. /beastmastr room opens it anyway."
                     : null;

        if (hidden != null)
        {
            Status = hidden;
            Close();
            return;
        }

        window ??= Build();

        if (!window.IsOpen)
            window.Open();

        var move = Shown();
        if (board.Rooms.Count == 0 || move < 0)
        {
            Status = "Open, but no board has been read yet.";
            Show("No board yet", "Open the Board Layout once — at the entrance, before a run — " +
                                 "and the board is kept from then on.", -1);
            return;
        }

        Status = board.CurrentMove < 0
                     ? $"Open, briefing move {move}. The run has not been located yet, so this is the first room."
                     : $"Open, briefing move {move}, the room the board marks as yours.";

        var text = Describe(move);
        Show($"Move {move}", text, move);
    }

    private void Show(string title, string text, int move)
    {
        if (window == null || text == drawn)
            return;

        drawn = text;
        window.Show(title, text, move > board.NextMove, move >= 0 && board.After(move) > 0);
    }

    private NextRoomAddon Build() =>
        new()
        {
            InternalName = "BeastMastrRoom",
            Title = "Next Room",
            Size = new Vector2(360f, 320f),
            RememberClosePosition = true,
            OpenInBounds = true,
            OnPrevious = () => Step(-1),
            OnNext = () => Step(1),
            OnWalk = () => walkNext(),
        };

    /// <summary>The move the panel is showing: the next one, plus however far the arrows have walked.</summary>
    private int Shown()
    {
        var move = board.NextMove;

        for (var step = 0; step < lookAhead && move > 0; step++)
            move = board.After(move);

        return move;
    }

    private void Step(int direction)
    {
        var was = lookAhead;
        lookAhead = Math.Max(0, lookAhead + direction);

        // Walking past the end of the board leaves nothing to show, so it is not allowed to.
        if (Shown() < 0)
            lookAhead = was;

        drawn = string.Empty;
        ticksUntilRecheck = 0;
    }

    /// <summary>
    /// What the room holds. Where a move branches, both options are described — choosing between
    /// them is the whole reason to look.
    /// </summary>
    private string Describe(int move)
    {
        var rooms = board.OnMove(move);
        if (rooms.Count == 0)
            return "Nothing on this move.";

        var text = new List<string>();

        foreach (var room in rooms)
        {
            if (text.Count > 0)
                text.Add(string.Empty);

            var mark = RouteMark(room);
            text.Add(rooms.Count > 1 ? $"— {mark}{room.Label} —" : mark + room.Label);

            if (room.Detail.Length > 0)
                text.Add(room.Detail);

            var brief = enemies.Brief(room.Label);
            if (brief.Count == 0)
            {
                // Said plainly rather than left blank. A room with nothing written under it reads as
                // a room with nothing in it, and those two are worth telling apart.
                if (Fights(room.Kind))
                    text.Add("Enemies not read yet — open the board and click this room once.");

                continue;
            }

            var needs = RoomBriefing.Needs(brief);
            if (needs.Count > 0)
                text.Add($"Bring: {string.Join("  ·  ", needs)}");

            var weaknesses = RoomBriefing.Weaknesses(brief);
            if (weaknesses.Count > 0)
                text.Add($"Weak to: {string.Join(", ", weaknesses)}");

            text.Add(string.Empty);
            text.AddRange(RoomBriefing.Lines(brief));
        }

        return string.Join("\n", text).TrimEnd();
    }

    /// <summary>
    /// "★ " for a room picked on the board, "▶ " for one the route takes, nothing otherwise. The saved
    /// room list counts back from the boss, which is how its entries are matched to the board's events;
    /// the move and kind have to agree as well, or no mark is claimed.
    /// </summary>
    private string RouteMark(StageDetailReader.Room room)
    {
        if (model.Graph is not { } graph)
            return string.Empty;

        var eventIndex = graph.MaxEventIndex - room.Index;
        if (graph.Node(eventIndex) is not { } node || node.Move != room.Move || (int)node.Kind != (int)room.Kind)
            return string.Empty;

        return route.IsChosen(eventIndex) ? "★ " : route.IsPlanned(eventIndex) ? "▶ " : string.Empty;
    }

    private static bool Fights(XbmColumns.RoomKind kind) =>
        kind is XbmColumns.RoomKind.Enemy or XbmColumns.RoomKind.EliteEnemy or XbmColumns.RoomKind.Boss
             or XbmColumns.RoomKind.RandomEnemyOrTreasure;

    private void Close()
    {
        if (window is { IsOpen: true })
            window.Close();

        drawn = string.Empty;
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnUpdate;

        window?.Dispose();
        window = null;
    }
}

/// <summary>
/// The window itself, drawn with the game's own frame rather than as an overlay of ours — which is
/// also what makes placement a non-question here: it is dragged where you want it and remembers.
///
/// Its nodes only exist between setup and finalize, so everything written to them goes through
/// <see cref="Show"/>, which keeps what it was given and applies it whenever the nodes are there.
/// </summary>
internal sealed unsafe class NextRoomAddon : NativeAddon
{
    private const uint HeadlineNodeId = 0x42470001;
    private const uint BodyNodeId = 0x42470002;
    private const uint PreviousNodeId = 0x42470003;
    private const uint NextNodeId = 0x42470004;
    private const uint WalkNodeId = 0x42470005;
    private const float WalkWidth = 80f;

    private const float ArrowWidth = 40f;
    private const float ArrowHeight = 24f;
    private const float HeadlineHeight = 22f;

    private TextNode? headline;
    private TextNode? body;
    private TextButtonNode? previous;
    private TextButtonNode? next;
    private TextButtonNode? walk;

    private string headlineText = string.Empty;
    private string bodyText = string.Empty;
    private bool canGoBack;
    private bool canGoOn;

    public Action? OnPrevious { get; init; }
    public Action? OnNext { get; init; }

    /// <summary>Walks to the route's next room — the same as /beastmastr step.</summary>
    public Action? OnWalk { get; init; }

    public void Show(string title, string text, bool back, bool on)
    {
        headlineText = title;
        bodyText = text;
        canGoBack = back;
        canGoOn = on;

        Apply();
    }

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
    {
        var start = ContentStartPosition;
        var size = ContentSize;

        headline = new TextNode
        {
            NodeId = HeadlineNodeId,
            Position = start,
            Size = new Vector2(size.X, HeadlineHeight),
            FontSize = 14,
            TextColor = new Vector4(1f, 0.90f, 0.55f, 1f),
            TextOutlineColor = new Vector4(0f, 0f, 0f, 1f),
            IsVisible = true,
        };

        body = new TextNode
        {
            NodeId = BodyNodeId,
            Position = start + new Vector2(0f, HeadlineHeight + 4f),
            Size = new Vector2(size.X, size.Y - HeadlineHeight - ArrowHeight - 12f),
            FontSize = 12,
            TextFlags = TextFlags.MultiLine | TextFlags.WordWrap,
            LineSpacing = 16,
            TextColor = new Vector4(1f, 1f, 1f, 1f),
            TextOutlineColor = new Vector4(0f, 0f, 0f, 1f),
            IsVisible = true,
        };

        previous = new TextButtonNode
        {
            NodeId = PreviousNodeId,
            Position = start + new Vector2(0f, size.Y - ArrowHeight),
            Size = new Vector2(ArrowWidth, ArrowHeight),
            String = "◀",
            IsVisible = true,
            OnClick = () => OnPrevious?.Invoke(),
        };

        next = new TextButtonNode
        {
            NodeId = NextNodeId,
            Position = start + new Vector2(ArrowWidth + 4f, size.Y - ArrowHeight),
            Size = new Vector2(ArrowWidth, ArrowHeight),
            String = "▶",
            IsVisible = true,
            OnClick = () => OnNext?.Invoke(),
        };

        walk = new TextButtonNode
        {
            NodeId = WalkNodeId,
            Position = start + new Vector2(size.X - WalkWidth, size.Y - ArrowHeight),
            Size = new Vector2(WalkWidth, ArrowHeight),
            String = "Walk",
            IsVisible = true,
            OnClick = () => OnWalk?.Invoke(),
        };

        AddNode(headline);
        AddNode(body);
        AddNode(previous);
        AddNode(next);
        AddNode(walk);

        Apply();
    }

    private void Apply()
    {
        if (headline == null || body == null || previous == null || next == null)
            return;

        headline.String = headlineText;
        body.String = bodyText;
        previous.IsVisible = canGoBack;
        next.IsVisible = canGoOn;
    }
}

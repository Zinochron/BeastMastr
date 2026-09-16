using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text;
using Dalamud.Game.Chat;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace BeastMastr.Data;

/// <summary>
/// A timeline of a whole run, written as it happens.
///
/// The board automation needs answers nothing captured so far holds: what "Commence Battle" sends,
/// what closes the spoils, what the job actually presses and what its gauge does meanwhile, which
/// objects a fight spawns. A single capture shows one moment, and every one of those questions is
/// about a sequence. So this records the sequence — one board played by hand with this on answers
/// all of them at once.
///
/// It only watches. The file is written line by line and flushed every second, so a crash mid-run
/// still leaves everything up to it on disk.
/// </summary>
public sealed unsafe class RunRecorder : IDisposable
{
    /// <summary>Windows whose values are dumped whenever they open, and diffed while they stay open.</summary>
    private static readonly string[] DumpedPrefixes =
        ["XBM", "JobHudXBM", "SelectYesno", "SelectString", "SelectIconString", "Talk", "ContextMenu"];

    /// <summary>Chat that says what the game did. Player chat stays out of a file meant to be shared.</summary>
    private static readonly XivChatType[] GameChat =
    [
        XivChatType.SystemMessage, XivChatType.ErrorMessage, XivChatType.LootNotice, XivChatType.LootRoll,
        XivChatType.NPCDialogue, XivChatType.NPCDialogueAnnouncements, XivChatType.SystemError,
    ];

    private const double PositionIntervalMs = 100;
    private const double ObjectIntervalMs = 250;
    private const double ValueDiffIntervalMs = 500;
    private const double FlushIntervalMs = 1000;
    private const int FramesBetweenWindowScans = 5;

    /// <summary>A seatbelt: a value diff of a window that rewrites itself constantly must not flood the file.</summary>
    private const int MostChangedValuesPerLine = 40;

    private readonly EventRecorder events;
    private readonly ActionWatcher actions;
    private readonly ConditionFlag[] flags = Enum.GetValues<ConditionFlag>();

    private StreamWriter? writer;
    private DateTime startedAt;
    private DateTime lastFlush;
    private bool eventRecorderWasOn;

    private readonly HashSet<ConditionFlag> activeFlags = [];
    private readonly HashSet<string> openWindows = [];
    private readonly HashSet<string> nodesDumped = [];
    private readonly Dictionary<string, List<string>> lastValues = [];
    private readonly Dictionary<ulong, string> objects = [];
    private readonly Dictionary<uint, string> playerStatuses = [];

    private uint territory;
    private uint comboAction;
    private string gauge = string.Empty;
    private string pets = string.Empty;
    private int currentEvent = -2;
    private string target = string.Empty;
    private string cast = string.Empty;
    private Vector3 lastPosition = new(float.NaN);
    private DateTime lastPositionAt;
    private DateTime lastObjectsAt;
    private DateTime lastValuesAt;
    private int framesUntilWindowScan;

    /// <summary>
    /// "REC" in the server info bar for as long as a recording runs, so there is no doubt whether one
    /// is. The command toggles, and typing it twice without seeing anything stops what the first
    /// started. Clicking the entry stops the recording.
    /// </summary>
    private readonly IDtrBarEntry? indicator;

    private int lastIndicatorSecond = -1;

    public RunRecorder(EventRecorder events, ActionWatcher actions)
    {
        this.events = events;
        this.actions = actions;

        try
        {
            indicator = Services.DtrBar.Get("BeastMastr recording");
            indicator.Shown = false;
            indicator.Tooltip = "BeastMastr is recording the run. Click to stop.";
            indicator.OnClick = _ => Stop();
        }
        catch (Exception ex)
        {
            Services.Log.Warning($"No server info bar entry for the recorder: {ex.Message}");
        }
    }

    public bool Recording => writer != null;

    /// <summary>Where the current or last recording went.</summary>
    public string LastPath { get; private set; } = string.Empty;

    public int LinesWritten { get; private set; }

    public TimeSpan Elapsed => Recording ? DateTime.Now - startedAt : TimeSpan.Zero;

    public void Toggle()
    {
        if (Recording)
            Stop();
        else
            Start();
    }

    public void Start()
    {
        if (Recording)
            return;

        try
        {
            var path = Path.Combine(CaptureStore.Directory.FullName, $"run-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            writer = new StreamWriter(path, false, new UTF8Encoding(false));
            LastPath = path;
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Could not start the run recording.");
            LastPath = "could not be written — see the log";
            Tell($"The run recording could not be started: {ex.Message}", NotificationType.Error);
            return;
        }

        startedAt = DateTime.Now;
        lastFlush = startedAt;
        LinesWritten = 0;
        ResetState();

        eventRecorderWasOn = events.Recording;
        events.Recording = true;
        events.Recorded += OnEvent;
        actions.ActionUsed += OnAction;
        Services.Chat.ChatMessage += OnChat;
        Services.Framework.Update += OnUpdate;

        WriteHeader();
        ShowIndicator();
        Services.Log.Information($"Run recording started: {LastPath}");
        Tell($"Recording the run to {Path.GetFileName(LastPath)}. /beastmastr record again, or the REC entry " +
             "in the server info bar, stops it.", NotificationType.Info);
    }

    public void Stop()
    {
        if (writer == null)
            return;

        Services.Framework.Update -= OnUpdate;
        Services.Chat.ChatMessage -= OnChat;
        actions.ActionUsed -= OnAction;
        events.Recorded -= OnEvent;
        events.Recording = eventRecorderWasOn;

        Line("stop", $"recording ended after {Elapsed:hh\\:mm\\:ss}, {LinesWritten} lines");

        try
        {
            writer.Flush();
            writer.Dispose();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Closing the run recording failed.");
        }

        writer = null;
        if (indicator != null)
            indicator.Shown = false;

        Services.Log.Information($"Run recording written: {LastPath}");
        Tell($"Run recording written: {Path.GetFileName(LastPath)} ({LinesWritten} lines).", NotificationType.Success);
    }

    /// <summary>In chat and as a toast: whether a recording runs must never be a guess.</summary>
    private static void Tell(string message, NotificationType type)
    {
        Services.Chat.Print($"[BeastMastr] {message}");

        try
        {
            Services.Notifications.AddNotification(new Notification
            {
                Title = "BeastMastr",
                Content = message,
                Type = type,
            });
        }
        catch (Exception ex)
        {
            Services.Log.Warning($"Could not show a notification: {ex.Message}");
        }
    }

    private void ShowIndicator()
    {
        if (indicator == null)
            return;

        var elapsed = Elapsed;
        var second = (int)elapsed.TotalSeconds;
        if (second == lastIndicatorSecond && indicator.Shown)
            return;

        lastIndicatorSecond = second;
        indicator.Text = $"● REC {elapsed:mm\\:ss}";
        indicator.Shown = true;
    }

    private void ResetState()
    {
        activeFlags.Clear();
        openWindows.Clear();
        nodesDumped.Clear();
        lastValues.Clear();
        objects.Clear();
        playerStatuses.Clear();
        territory = uint.MaxValue;
        comboAction = uint.MaxValue;
        gauge = string.Empty;
        pets = string.Empty;
        currentEvent = -2;
        target = string.Empty;
        cast = string.Empty;
        lastPosition = new Vector3(float.NaN);
        lastPositionAt = DateTime.MinValue;
        lastObjectsAt = DateTime.MinValue;
        lastValuesAt = DateTime.MinValue;
        framesUntilWindowScan = 0;
    }

    // ---- Writing -----------------------------------------------------------

    private void Line(string kind, string text)
    {
        if (writer == null)
            return;

        writer.Write(DateTime.Now.ToString("HH:mm:ss.fff"));
        writer.Write('\t');
        writer.Write(kind);
        writer.Write('\t');
        writer.WriteLine(text);
        LinesWritten++;
    }

    private void Block(string text)
    {
        if (writer == null)
            return;

        writer.WriteLine(text);
        LinesWritten += text.Count(c => c == '\n') + 1;
    }

    private void WriteHeader()
    {
        var player = Services.Objects.LocalPlayer;
        Block($"# BeastMastr run recording {startedAt:yyyy-MM-dd HH:mm:ss}");
        Block($"# job={GaugeReader.PlayerJob} level={player?.Level} territory={Services.ClientState.TerritoryType}");
        Block($"# {MapMarkerReader.DescribeTransform()}");
        Block("# Columns: time, kind, text. Kinds: flag+/flag-, zone, open/close, values, nodes, diff, cb (callback " +
              "or input event), use (action), combo, gauge, pets, event (board's current event), target, cast, " +
              "status+/status-, pos, obj+/obj-/obj~, chat, board, marker");
        Block(string.Empty);
        WriteBoard();
    }

    /// <summary>The board as the window and the map have it at the start, so the timeline has something to refer to.</summary>
    private void WriteBoard()
    {
        var board = BoardEventMapReader.Read();
        if (board != null)
        {
            Line("board", $"{board.Addon} row={board.BoardRowId} grid={board.GridSize}/{board.GridHalfSize} " +
                          $"loaded={board.Loaded} current={board.CurrentEventIndex} cells={board.Cells.Count}");

            foreach (var cell in board.Cells)
                Line("board", $"cell {cell.Index} x={cell.X} y={cell.Y} type={cell.Type} event={cell.EventIndex} " +
                              $"linked={cell.LinkedEventIndex} state={cell.State}");
        }

        foreach (var marker in MapMarkerReader.Read())
        {
            Line("marker", $"{marker.Source} icon={marker.IconId} kind={marker.Kind} map={marker.MapX}/{marker.MapY} " +
                           $"world={marker.World?.X:0.00}/{marker.World?.Z:0.00}");
        }
    }

    // ---- Sources pushed to us ---------------------------------------------

    private void OnEvent(EventRecorder.Entry entry) =>
        Line("cb", $"{entry.Addon}\t{entry.EventType}\tparam={entry.EventParam}\t{entry.Detail}");

    private void OnAction(ActionWatcher.Use use)
    {
        var name = use.Type == ActionType.Action ? ActionName(use.ActionId) : string.Empty;
        Line("use", $"{use.Type} {use.ActionId} {name} target=0x{use.TargetId:X} mode={use.Mode} " +
                    $"accepted={use.Accepted}{(use.FromHotbar ? " hotbar" : string.Empty)}");
    }

    private void OnChat(IHandleableChatMessage message)
    {
        if (!GameChat.Contains(message.LogKind))
            return;

        Line("chat", $"{message.LogKind}\t{message.Sender.TextValue}\t{message.Message.TextValue}");
    }

    private static string ActionName(uint id) =>
        Services.Data.GetExcelSheet<LuminaAction>().GetRowOrDefault(id)?.Name.ExtractText() ?? string.Empty;

    // ---- Sources polled ----------------------------------------------------

    private void OnUpdate(IFramework framework)
    {
        try
        {
            var now = DateTime.Now;

            PollFlags();
            PollTerritory();

            if (--framesUntilWindowScan <= 0)
            {
                framesUntilWindowScan = FramesBetweenWindowScans;
                PollWindows();
            }

            PollCombat();
            PollBoard();

            if ((now - lastPositionAt).TotalMilliseconds >= PositionIntervalMs)
            {
                lastPositionAt = now;
                PollPosition();
            }

            if ((now - lastObjectsAt).TotalMilliseconds >= ObjectIntervalMs)
            {
                lastObjectsAt = now;
                PollObjects();
                PollStatuses();
            }

            if ((now - lastValuesAt).TotalMilliseconds >= ValueDiffIntervalMs)
            {
                lastValuesAt = now;
                PollValueDiffs();
            }

            if ((now - lastFlush).TotalMilliseconds >= FlushIntervalMs)
            {
                lastFlush = now;
                writer?.Flush();
                ShowIndicator();
            }
        }
        catch (Exception ex)
        {
            // One bad frame must not end a recording that took a whole board to make.
            Services.Log.Error(ex, "The run recorder failed a frame.");
        }
    }

    private void PollFlags()
    {
        foreach (var flag in flags)
        {
            var on = Services.Condition[flag];
            if (on == activeFlags.Contains(flag))
                continue;

            if (on)
                activeFlags.Add(flag);
            else
                activeFlags.Remove(flag);

            Line(on ? "flag+" : "flag-", flag.ToString());
        }
    }

    private void PollTerritory()
    {
        var now = Services.ClientState.TerritoryType;
        if (now == territory)
            return;

        territory = now;
        Line("zone", $"territory={now} {MapMarkerReader.DescribeTransform()}");
    }

    private void PollWindows()
    {
        var visible = VisibleWindows();

        foreach (var name in visible)
        {
            if (openWindows.Contains(name))
                continue;

            Line("open", name);

            if (Dumped(name))
            {
                // Unset values are most of every dump — 93% of the first recording — and say nothing.
                var values = AddonReader.Values(name);
                Block(AddonReader.ToText(name, values.Where(value => value.Type != "Undefined")));
                lastValues[name] = values.Select(value => $"{value.Type}={value.Text}").ToList();

                // The node tree only once per window per recording: it is long and does not change
                // its shape, while the values are what move.
                if (nodesDumped.Add(name))
                    Block(AddonReader.ToText(name, AddonReader.Nodes(name)));
            }
        }

        foreach (var name in openWindows)
        {
            if (visible.Contains(name))
                continue;

            Line("close", name);
            lastValues.Remove(name);
        }

        openWindows.Clear();
        openWindows.UnionWith(visible);
    }

    private static bool Dumped(string name) =>
        DumpedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    private static HashSet<string> VisibleWindows()
    {
        var names = new HashSet<string>();
        var stage = AtkStage.Instance();
        if (stage == null || stage->RaptureAtkUnitManager == null)
            return names;

        var units = stage->RaptureAtkUnitManager->AllLoadedUnitsList;
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units.Entries[i].Value;
            if (unit != null && unit->IsVisible)
                names.Add(unit->NameString);
        }

        return names;
    }

    private void PollValueDiffs()
    {
        foreach (var name in lastValues.Keys.ToList())
        {
            var now = AddonReader.Values(name).Select(value => $"{value.Type}={value.Text}").ToList();
            var before = lastValues[name];

            var changes = new List<string>();
            var total = 0;
            for (var i = 0; i < Math.Max(now.Count, before.Count); i++)
            {
                var was = i < before.Count ? before[i] : "(none)";
                var is_ = i < now.Count ? now[i] : "(none)";
                if (was == is_)
                    continue;

                total++;
                if (changes.Count < MostChangedValuesPerLine)
                    changes.Add($"[{i}] {was} -> {is_}");
            }

            if (total > 0)
                Line("diff", $"{name} {total} changed: {string.Join("; ", changes)}");

            lastValues[name] = now;
        }
    }

    private void PollCombat()
    {
        var manager = ActionManager.Instance();
        if (manager != null && manager->Combo.Action != comboAction)
        {
            comboAction = manager->Combo.Action;
            Line("combo", $"{comboAction} {ActionName(comboAction)} timer={manager->Combo.Timer:0.00}");
        }

        var raw = string.Join(" ", GaugeReader.Raw().Select(b => b.ToString("X2")));
        if (raw != gauge)
        {
            gauge = raw;
            Line("gauge", $"{raw}  {GaugeReader.Read()}");
        }

        var pet = string.Join(" ", GaugeReader.Pets());
        if (pet != pets)
        {
            pets = pet;
            Line("pets", pet);
        }

        var current = Services.Targets.Target;
        var targetText = current == null ? "none" : Describe(current);
        if (targetText != target)
        {
            target = targetText;
            Line("target", targetText);
        }

        var player = Services.Objects.LocalPlayer;
        var castText = player is { IsCasting: true } ? $"{player.CastActionId} {ActionName(player.CastActionId)}" : "none";
        if (castText != cast)
        {
            cast = castText;
            Line("cast", castText);
        }
    }

    private void PollBoard()
    {
        var board = BoardEventMapReader.Read();
        var now = board == null ? -1 : (board.MarkedEvent * 100) + board.CurrentEventIndex;
        if (now == currentEvent)
            return;

        currentEvent = now;
        Line("event", board == null
                          ? "no board window"
                          : $"selected={board.CurrentEventIndex} marked={board.MarkedEvent} row={board.BoardRowId} in {board.Addon}");
    }

    private void PollPosition()
    {
        var player = Services.Objects.LocalPlayer;
        if (player == null)
            return;

        var position = player.Position;
        if (!float.IsNaN(lastPosition.X) && Vector3.Distance(position, lastPosition) < 0.05f)
            return;

        lastPosition = position;
        Line("pos", $"{position.X:0.00} {position.Y:0.00} {position.Z:0.00} rot={player.Rotation:0.00} " +
                    $"hp={player.CurrentHp}/{player.MaxHp}");
    }

    private void PollObjects()
    {
        var seen = new HashSet<ulong>();

        foreach (var obj in Services.Objects)
        {
            if (obj.GameObjectId == Services.Objects.LocalPlayer?.GameObjectId)
                continue;

            seen.Add(obj.GameObjectId);
            var text = Describe(obj);

            if (!objects.TryGetValue(obj.GameObjectId, out var before))
                Line("obj+", text);
            else if (before != text && Changed(before, text))
                Line("obj~", text);

            objects[obj.GameObjectId] = text;
        }

        foreach (var gone in objects.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            Line("obj-", objects[gone]);
            objects.Remove(gone);
        }
    }

    /// <summary>
    /// A moving enemy changes its position every poll; only state changes are worth a line. Position
    /// is the last field, so comparing everything before it is enough.
    /// </summary>
    private static bool Changed(string before, string now) =>
        before[..before.LastIndexOf(" at ", StringComparison.Ordinal)] !=
        now[..now.LastIndexOf(" at ", StringComparison.Ordinal)];

    private static string Describe(IGameObject obj)
    {
        var text = new StringBuilder();
        text.Append($"id=0x{obj.GameObjectId:X} {obj.ObjectKind}/{obj.SubKind} \"{obj.Name.TextValue}\" base={obj.BaseId}");
        text.Append($" targetable={obj.IsTargetable} dead={obj.IsDead}");

        if (obj is IBattleChara battle)
        {
            text.Append($" hp={battle.CurrentHp}/{battle.MaxHp}");
            if (battle.IsCasting)
                text.Append($" casting={battle.CastActionId}");
        }

        text.Append($" at {obj.Position.X:0.0}/{obj.Position.Y:0.0}/{obj.Position.Z:0.0}");
        return text.ToString();
    }

    private void PollStatuses()
    {
        var player = Services.Objects.LocalPlayer;
        if (player == null)
            return;

        var now = new Dictionary<uint, string>();
        foreach (var status in player.StatusList)
        {
            if (status.StatusId == 0)
                continue;

            now[status.StatusId] = $"{status.StatusId} {status.GameData.Value.Name.ExtractText()} param={status.Param}";
        }

        foreach (var (id, text) in now)
        {
            if (!playerStatuses.ContainsKey(id))
                Line("status+", text);
        }

        foreach (var (id, text) in playerStatuses)
        {
            if (!now.ContainsKey(id))
                Line("status-", text);
        }

        playerStatuses.Clear();
        foreach (var (id, text) in now)
            playerStatuses[id] = text;
    }

    public void Dispose()
    {
        Stop();
        indicator?.Remove();
    }
}

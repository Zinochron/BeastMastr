using System;
using System.Collections.Generic;
using BeastMastr.Rules;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace BeastMastr.Data;

/// <summary>
/// Counts the boards finished and the loot rolled for at their end — the remnants of resilience and the
/// Modern Aesthetics items — kept in the configuration across sessions, and for this session alone.
///
/// Only what was put on the loot list is counted: a room's spoils ("… as loot."), gil and tokens are not.
/// A board is counted as finished when its result window opens, won or lost, run by hand or not.
/// </summary>
public sealed class LootTracker : IDisposable
{
    private readonly Configuration configuration;

    /// <summary>Names put on the loot list and not yet obtained, as the item's own name.</summary>
    private readonly HashSet<string> onTheList = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The name as a chat line gives it — singular or plural — to the item's own name.</summary>
    private readonly Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);

    private bool resultWasOpen;

    public LootTracker(Configuration configuration)
    {
        this.configuration = configuration;
        Services.Chat.ChatMessage += OnChat;
        Services.Framework.Update += OnUpdate;
    }

    /// <summary>Boards finished since the plugin loaded.</summary>
    public int BoardsThisSession { get; private set; }

    /// <summary>Loot obtained since the plugin loaded, by item name.</summary>
    public Dictionary<string, int> ThisSession { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Forgets everything counted, this session's and the saved totals.</summary>
    public void Reset()
    {
        ThisSession.Clear();
        BoardsThisSession = 0;
        BoardTimes.Clear();
        SessionStartedAt = BoardStartedAt;
        LastFinishedAt = null;
        configuration.LootTotals.Clear();
        configuration.BoardsFinished = 0;
        configuration.TimedBoards = 0;
        configuration.TimedBoardSeconds = 0;
        configuration.Save();
    }

    /// <summary>When the board being played was entered, or null off a board.</summary>
    public DateTime? BoardStartedAt { get; private set; }

    /// <summary>How long this session's finished boards took, entering to result, in order.</summary>
    public List<TimeSpan> BoardTimes { get; } = [];

    /// <summary>When this session's first timed board was entered: boards per hour count from here.</summary>
    public DateTime? SessionStartedAt { get; private set; }

    /// <summary>When this session's last board was finished.</summary>
    public DateTime? LastFinishedAt { get; private set; }

    private bool wasOnBoard;

    private void OnUpdate(IFramework framework)
    {
        var now = DateTime.Now;
        var onBoard = BoardModel.IsRunTerritory(Services.ClientState.TerritoryType);
        if (onBoard && !wasOnBoard)
        {
            BoardStartedAt = now;
            SessionStartedAt ??= now;
        }
        else if (!onBoard && wasOnBoard)
        {
            BoardStartedAt = null;
        }

        wasOnBoard = onBoard;

        var open = AddonReader.IsOpen(XbmColumns.RunWindows.Result);
        if (open && !resultWasOpen && onBoard)
        {
            BoardsThisSession++;
            configuration.BoardsFinished++;
            LastFinishedAt = now;

            // A board is only timed when it was entered while the plugin watched: one it was loaded into
            // half way would count as a fast board.
            if (BoardStartedAt is { } started)
            {
                var took = now - started;
                BoardTimes.Add(took);
                configuration.TimedBoards++;
                configuration.TimedBoardSeconds += took.TotalSeconds;
                Services.Log.Information($"Loot: board finished in {took:mm\\:ss} ({configuration.BoardsFinished} in all).");
            }

            configuration.Save();
        }

        resultWasOpen = open;
    }

    private void OnChat(IHandleableChatMessage message)
    {
        try
        {
            var text = message.Message.TextValue;
            if (message.LogKind == XivChatType.SystemMessage && LootLog.Added(text) is { } added)
            {
                onTheList.Add(ItemName(added.Name));
                return;
            }

            if (message.LogKind != XivChatType.LootNotice || LootLog.Obtained(text) is not { } obtained)
                return;

            var name = ItemName(obtained.Name);
            if (!onTheList.Remove(name))
                return;

            ThisSession[name] = ThisSession.GetValueOrDefault(name) + obtained.Count;
            configuration.LootTotals[name] = configuration.LootTotals.GetValueOrDefault(name) + obtained.Count;
            configuration.Save();
            Services.Log.Information($"Loot: {obtained.Count} × {name} ({configuration.LootTotals[name]} in all).");
        }
        catch (Exception ex)
        {
            Services.Log.Warning($"Loot: could not read \"{message.Message.TextValue}\": {ex.Message}");
        }
    }

    /// <summary>
    /// The item's own name for a chat line's "bright remnants of resilience": the Item sheet's singular and
    /// plural are what the chat uses, its name is what is shown. Looked up once per spelling.
    /// </summary>
    private string ItemName(string said)
    {
        if (names.TryGetValue(said, out var known))
            return known;

        var found = said;
        foreach (var item in Services.Data.GetExcelSheet<LuminaItem>())
        {
            if (string.Equals(item.Singular.ExtractText(), said, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Plural.ExtractText(), said, StringComparison.OrdinalIgnoreCase))
            {
                found = item.Name.ExtractText();
                break;
            }
        }

        names[said] = found;
        return found;
    }

    public void Dispose()
    {
        Services.Chat.ChatMessage -= OnChat;
        Services.Framework.Update -= OnUpdate;
    }
}

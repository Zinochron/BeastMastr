using System;
using System.Linq;
using BeastMastr.Data;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Automation;

/// <summary>
/// Reads every beast's rank out of the bestiary in one pass, on request.
///
/// The ranks do exist as data — the game knows all fifty — but the only place found that shows one
/// is the detail page, for whichever beast the cursor is on. So this moves the cursor: it sends the
/// window the same <c>[5, slot]</c> the mouse sends when it enters a tile, waits for the page to
/// catch up, and reads.
///
/// **It changes nothing.** Hovering is not selecting: no beast joins or leaves a team, and the page
/// is put back where it started. That is why this is worth doing at all — the alternative, clicking
/// each beast, would rearrange a team to answer a question about it.
/// </summary>
public sealed unsafe class RankPuller : IDisposable
{
    /// <summary>Frames to let the detail page catch up with the cursor before reading it.</summary>
    private const int FramesPerSlot = 6;

    private readonly RankWatcher ranks;

    private int slot = -1;
    private int page;
    private int startingPage;
    private int cooldown;
    private int learned;

    public RankPuller(RankWatcher ranks)
    {
        this.ranks = ranks;
        Services.Framework.Update += OnUpdate;
    }

    public bool Running => slot >= 0;

    public string Status { get; private set; } = string.Empty;

    public void Start()
    {
        if (Running || !AddonReader.IsOpen(XbmColumns.MonsterNotebook.Addon))
        {
            Status = "The bestiary has to be open.";
            return;
        }

        // Every rank a board shows is synced to that board's own, so reading them there would write
        // the whole roster down. Outside, the numbers are the beasts' own.
        if (Data.BoardModel.IsRunTerritory(Services.ClientState.TerritoryType))
        {
            Status = "Not on a board: the ranks shown here are synced to it. Read them outside.";
            return;
        }

        startingPage = Math.Max(0, CurrentPage());
        page = 0;
        slot = 0;
        learned = 0;
        cooldown = 0;

        Status = "Reading ranks…";
    }

    public void Cancel()
    {
        slot = -1;
        Status = "Stopped.";
    }

    private void OnUpdate(IFramework framework)
    {
        if (!Running || cooldown-- > 0)
            return;

        if (!AddonReader.TryGet(XbmColumns.MonsterNotebook.Addon, out var addon))
        {
            Finish("The bestiary closed part way through.");
            return;
        }

        // Read whatever the previous hover put on the page before moving on.
        Read();

        if (slot >= XbmColumns.MonsterNotebook.TileCount)
        {
            slot = 0;
            page++;

            if (page > XbmColumns.MonsterNotebook.PageOf(50))
            {
                Send(addon, XbmColumns.MonsterNotebook.TurnPageCommand, startingPage);
                Finish($"Read {learned} rank(s).");
                return;
            }

            Send(addon, XbmColumns.MonsterNotebook.TurnPageCommand, page);
            cooldown = FramesPerSlot * 2;
            return;
        }

        Send(addon, XbmColumns.MonsterNotebook.HoverCommand, slot);
        slot++;
        cooldown = FramesPerSlot;

        Status = $"Reading ranks… {learned} so far.";
    }

    /// <summary>
    /// The detail page as it stands. Both numbers have to be there and the panel has to be showing,
    /// or a hidden panel's leftovers get attributed to whichever beast is under the cursor now.
    /// </summary>
    private void Read()
    {
        var addon = XbmColumns.MonsterBookDetail.Addon;

        if (!AddonReader.IsOpen(addon)
            || !AddonReader.IsNodeVisible(addon, (uint)XbmColumns.MonsterBookDetail.RankPanelNodeId))
            return;

        var number = Digits(AddonReader.TextOf(addon, XbmColumns.MonsterBookDetail.NumberNodeId));
        var rank = Digits(AddonReader.TextOf(addon, XbmColumns.MonsterBookDetail.RankValueNodeId));

        if (number is > 0 and <= 50 && rank > 0 && ranks.Learn((uint)number, rank))
            learned++;
    }

    private static int Digits(string text)
    {
        var slash = text.IndexOf('/');
        if (slash >= 0)
            text = text[..slash];

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    private static int CurrentPage()
    {
        var addon = AddonReader.Find(XbmColumns.MonsterNotebook.Addon);
        if (addon.IsNull)
            return 0;

        var values = addon.AtkValues.ToList();
        var index = XbmColumns.MonsterNotebook.SlotNumberValue(0);

        return index < values.Count && values[index].TryGet<uint>(out var number) && number > 0
                   ? XbmColumns.MonsterNotebook.PageOf(number)
                   : 0;
    }

    private static void Send(AtkUnitBase* addon, int command, int argument)
    {
        var values = stackalloc AtkValue[2];
        values[0].SetInt(command);
        values[1].SetUInt((uint)argument);

        addon->FireCallback(2, values);
    }

    private void Finish(string message)
    {
        slot = -1;
        Status = message;
        Services.Log.Information($"Rank sweep: {message}");
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}

using System;
using System.Collections.Generic;
using BeastMastr.Data;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Automation.Run;

/// <summary>
/// What a room's buttons send, as recorded from real clicks (<c>captures/run-20260916-170423.txt</c>) —
/// and nothing that has not been recorded.
///
/// Every one of these was sent by the game in the recording with exactly these types, and every one
/// that asks "are you sure" was followed by a <c>SelectYesno</c> answered with <c>[0]</c>. Until a
/// command is recorded it stays null and the run hands that step to the player: a guessed payload is
/// ignored at best and does something else at worst.
/// </summary>
public static unsafe class RoomActions
{
    public const string YesnoAddon = "SelectYesno";

    /// <summary>One value of a callback, typed as the recording showed it.</summary>
    public readonly record struct Value(AtkValueType Type, int Number)
    {
        public static Value Int(int number) => new(AtkValueType.Int, number);

        public static Value UInt(uint number) => new(AtkValueType.UInt, (int)number);
    }

    /// <param name="Label">What the button is called, for the log and for the player when the step is handed over.</param>
    /// <param name="AsksFirst">A <c>SelectYesno</c> follows, and has to be answered for the command to happen.</param>
    /// <param name="MayAsk">
    /// A <c>SelectYesno</c> may follow, and is answered yes if it does; otherwise the window closing is enough.
    /// </param>
    public sealed record Command(string Label, string Addon, IReadOnlyList<Value> Values, bool Closes, bool AsksFirst,
                                 bool MayAsk = false);

    /// <summary>
    /// "Commence Battle" in the board window, once the familiars are called. With a Battlehorn left
    /// empty the game asks "At least one battlehorn has not been assigned. Commence battle anyway?" —
    /// by then every familiar that can fight is called, so the answer is yes.
    /// </summary>
    public static readonly Command CommenceBattle =
        new("Commence Battle", XbmColumns.StageDetailList.Addon,
            [Value.Int(XbmColumns.StageDetailList.CommenceBattleCommand)], true, false, MayAsk: true);

    /// <summary>Taking everything the spoils offer.</summary>
    public static readonly Command TakeSpoils =
        new("take the spoils", XbmColumns.RunWindows.Booty,
            [Value.Int(XbmColumns.RunWindows.TakeSpoilsCommand)], true, true);

    /// <summary>Resting at a campsite, with whoever is picked — or alone, when nobody is.</summary>
    public static readonly Command ConfirmCampsite =
        new("rest at the campsite", XbmColumns.PetParty.Addon,
            [Value.Int(XbmColumns.PetParty.ConfirmCommand)], true, true);

    /// <summary>Leaving a shop.</summary>
    public static readonly Command LeaveShop =
        new("leave the shop", XbmColumns.RunWindows.ItemShop,
            [Value.Int(XbmColumns.RunWindows.LeaveShopCommand)], true, true);

    /// <summary>Taking offer <paramref name="index"/> of a treasure coffer, counted from 0.</summary>
    public static Command ChooseTreasure(int index) =>
        new($"take treasure offer {index + 1}", XbmColumns.RunWindows.Treasure,
            [Value.Int(XbmColumns.RunWindows.ChooseTreasureCommand), Value.Int(index)], true, true);

    /// <summary>"Yes" in a <c>SelectYesno</c>.</summary>
    public static readonly Command Yes =
        new("Yes", YesnoAddon, [Value.Int(XbmColumns.RunWindows.Yes)], true, false);

    /// <summary>"OK" in a <c>SelectOk</c>, as recorded after picking gear already held.</summary>
    public static readonly Command Ok =
        new("OK", XbmColumns.RunWindows.SelectOk, [Value.Int(0)], true, false);

    /// <summary>
    /// Closing the board's result. Not recorded: in the recording no result window appeared — the
    /// board ended with a cutscene and a load back to the entrance.
    /// </summary>
    public static Command? CloseResult => null;

    /// <summary>Sends a recorded command. False when its window is not up.</summary>
    public static bool Send(Command command)
    {
        if (!AddonReader.IsOpen(command.Addon) || !AddonReader.TryGet(command.Addon, out var addon))
            return false;

        var count = command.Values.Count;
        var values = stackalloc AtkValue[count];

        for (var i = 0; i < count; i++)
        {
            var value = command.Values[i];
            if (value.Type == AtkValueType.UInt)
                values[i].SetUInt((uint)value.Number);
            else
                values[i].SetInt(value.Number);
        }

        addon->FireCallback((uint)count, values, command.Closes);
        Services.Log.Information($"Sent \"{command.Label}\" to {command.Addon}.");
        return true;
    }

    /// <summary>One offer of a treasure coffer.</summary>
    /// <param name="Held">Beast Gear already held; the game refuses to hand it out twice.</param>
    public sealed record Offer(int Index, uint Item, string Name, bool IsGear, bool Held = false);

    /// <summary>
    /// The treasure coffer's offers: position and <c>XBMItem</c> row, for the ones the window marks as
    /// offered. Empty when the window is not up.
    /// </summary>
    public static List<Offer> TreasureOffers()
    {
        var offers = new List<Offer>();
        if (!AddonReader.IsOpen(XbmColumns.RunWindows.Treasure) ||
            !AddonReader.TryGet(XbmColumns.RunWindows.Treasure, out var addon))
            return offers;

        var held = new HashSet<uint>();
        for (var block = 0; block < XbmColumns.RunWindows.TreasureHeldGearMax; block++)
        {
            var start = XbmColumns.RunWindows.TreasureFirstHeldGear + (block * XbmColumns.RunWindows.TreasureHeldGearStride);
            var item = start + XbmColumns.RunWindows.TreasureHeldGearItemOffset;
            if (item >= addon->AtkValuesCount)
                break;

            var used = addon->AtkValues[start];
            var id = addon->AtkValues[item];
            if (used.Type != AtkValueType.Bool || used.Byte == 0 || id.Type != AtkValueType.UInt)
                break;

            held.Add(id.UInt);
        }

        for (var i = 0; i < XbmColumns.RunWindows.TreasureOffers; i++)
        {
            var start = XbmColumns.RunWindows.TreasureFirstOffer + (i * XbmColumns.RunWindows.TreasureOfferStride);
            var item = start + XbmColumns.RunWindows.TreasureItemOffset;
            if (item >= addon->AtkValuesCount)
                break;

            var offered = addon->AtkValues[start];
            var id = addon->AtkValues[item];
            if (offered.Type == AtkValueType.Bool && offered.Byte != 0 && id.Type == AtkValueType.UInt && id.UInt != 0)
            {
                var offer = Describe(i, id.UInt);
                offers.Add(offer with { Held = offer.IsGear && held.Contains(id.UInt) });
            }
        }

        return offers;
    }

    private static Offer Describe(int index, uint item)
    {
        try
        {
            var sheet = Services.Data.Excel.GetSheet<Lumina.Excel.RawRow>(null, XbmColumns.XbmItem.Sheet);
            if (sheet.TryGetRow(item, out var row))
            {
                return new Offer(index, item, row.ReadStringColumn(XbmColumns.XbmItem.DisplayName).ExtractText(),
                                 Convert.ToInt32(row.ReadColumn(XbmColumns.XbmItem.Kind)) == XbmColumns.XbmItem.GearKind);
            }
        }
        catch (Exception ex)
        {
            Services.Log.Warning($"Could not read item {item}: {ex.Message}");
        }

        return new Offer(index, item, $"item {item}", false);
    }
}

/// <summary>
/// One recorded command carried through: sent, its "are you sure" answered, and the window seen to
/// close. Ticked by the run; says when it is done and why it gave up.
/// </summary>
public sealed class ConfirmedStep
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>How long a window may be up but not yet ready before the command counts as not sendable.</summary>
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(2);

    /// <summary>A yes that did not close the question is sent again after this long.</summary>
    private static readonly TimeSpan RepeatYesAfter = TimeSpan.FromSeconds(1);

    private readonly RoomActions.Command command;
    private readonly DateTime created = DateTime.Now;
    private DateTime sentAt;
    private int stage;

    public ConfirmedStep(RoomActions.Command command) => this.command = command;

    public bool Done { get; private set; }

    /// <summary>Why the step could not be carried through, or null while it can.</summary>
    public string? Failure { get; private set; }

    /// <summary>The game said no with a <c>SelectOk</c> — gear already held — and that notice is closed.</summary>
    public bool Refused { get; private set; }

    public string Label => command.Label;

    public void Tick()
    {
        if (Done || Failure != null || Refused)
            return;

        var now = DateTime.Now;
        switch (stage)
        {
            // Sent once the window has been up a moment, so it has its values.
            case 0:
                if (now - created < FirstDelay)
                    return;

                // A window that is visible is not always loaded yet; that is waited out, not failed.
                if (!RoomActions.Send(command))
                {
                    if (now - created > FirstDelay + ReadyTimeout)
                        Failure = $"The window for \"{command.Label}\" is not up.";

                    return;
                }

                sentAt = now;
                stage = command.AsksFirst || command.MayAsk ? 1 : 2;
                return;

            // The question has to come from this command: only a SelectYesno that opens after it is
            // answered. It can be visible a frame before it takes an answer, so the step only moves on
            // once the answer was actually sent — the first run recording lost a spoils window that way.
            case 1:
                if (AddonReader.IsOpen(XbmColumns.RunWindows.SelectOk))
                {
                    if (RoomActions.Send(RoomActions.Ok))
                        Refused = true;

                    return;
                }

                if (AddonReader.IsOpen(RoomActions.YesnoAddon))
                {
                    if (RoomActions.Send(RoomActions.Yes))
                    {
                        sentAt = now;
                        stage = 2;
                    }

                    return;
                }

                if (command.MayAsk && !AddonReader.IsOpen(command.Addon))
                {
                    Done = true;
                    return;
                }

                if (now - sentAt > AskTimeout)
                {
                    Failure = command.MayAsk
                                  ? $"\"{command.Label}\" was sent, but its window stayed open."
                                  : $"\"{command.Label}\" was sent, but nothing asked to confirm it.";
                }

                return;

            case 2:
                if (!AddonReader.IsOpen(command.Addon))
                {
                    Done = true;
                    return;
                }

                if (AddonReader.IsOpen(RoomActions.YesnoAddon) && now - sentAt > RepeatYesAfter &&
                    RoomActions.Send(RoomActions.Yes))
                    sentAt = now;

                if (now - sentAt > CloseTimeout)
                    Failure = $"\"{command.Label}\" was sent and confirmed, but its window stayed open.";

                return;
        }
    }
}

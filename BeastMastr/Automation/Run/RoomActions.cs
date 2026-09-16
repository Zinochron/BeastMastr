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
    public sealed record Command(string Label, string Addon, IReadOnlyList<Value> Values, bool Closes, bool AsksFirst);

    /// <summary>"Commence Battle" in the board window, once the familiars are called.</summary>
    public static readonly Command CommenceBattle =
        new("Commence Battle", XbmColumns.StageDetailList.Addon,
            [Value.Int(XbmColumns.StageDetailList.CommenceBattleCommand)], true, false);

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
    public sealed record Offer(int Index, uint Item, string Name, bool IsGear);

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

        for (var i = 0; i < XbmColumns.RunWindows.TreasureOffers; i++)
        {
            var start = XbmColumns.RunWindows.TreasureFirstOffer + (i * XbmColumns.RunWindows.TreasureOfferStride);
            var item = start + XbmColumns.RunWindows.TreasureItemOffset;
            if (item >= addon->AtkValuesCount)
                break;

            var offered = addon->AtkValues[start];
            var id = addon->AtkValues[item];
            if (offered.Type == AtkValueType.Bool && offered.Byte != 0 && id.Type == AtkValueType.UInt && id.UInt != 0)
                offers.Add(Describe(i, id.UInt));
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

    private readonly RoomActions.Command command;
    private readonly DateTime created = DateTime.Now;
    private DateTime sentAt;
    private int stage;

    public ConfirmedStep(RoomActions.Command command) => this.command = command;

    public bool Done { get; private set; }

    /// <summary>Why the step could not be carried through, or null while it can.</summary>
    public string? Failure { get; private set; }

    public string Label => command.Label;

    public void Tick()
    {
        if (Done || Failure != null)
            return;

        var now = DateTime.Now;
        switch (stage)
        {
            // Sent once the window has been up a moment, so it has its values.
            case 0:
                if (now - created < FirstDelay)
                    return;

                if (!RoomActions.Send(command))
                {
                    Failure = $"The window for \"{command.Label}\" is not up.";
                    return;
                }

                sentAt = now;
                stage = command.AsksFirst ? 1 : 2;
                return;

            // The question has to come from this command: only a SelectYesno that opens after it is answered.
            case 1:
                if (AddonReader.IsOpen(RoomActions.YesnoAddon))
                {
                    RoomActions.Send(RoomActions.Yes);
                    sentAt = now;
                    stage = 2;
                    return;
                }

                if (now - sentAt > AskTimeout)
                    Failure = $"\"{command.Label}\" was sent, but nothing asked to confirm it.";

                return;

            case 2:
                if (!AddonReader.IsOpen(command.Addon))
                {
                    Done = true;
                    return;
                }

                if (now - sentAt > CloseTimeout)
                    Failure = $"\"{command.Label}\" was sent and confirmed, but its window stayed open.";

                return;
        }
    }
}

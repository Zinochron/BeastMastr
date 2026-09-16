using System.Collections.Generic;
using BeastMastr.Data;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Automation.Run;

/// <summary>
/// What a room's buttons send, as recorded from real clicks — and nothing that has not been recorded.
///
/// Every command here is to be copied out of a run recording (<c>/beastmastr record</c>): the window,
/// each value with its type, and whether the click closed the window. Until one is, it stays null, and
/// the run hands that step to the player instead of guessing — a guessed payload is ignored at best and
/// does something else at worst, and this plugin's history has examples of both.
/// </summary>
public static unsafe class RoomActions
{
    /// <summary>One value of a callback, typed as the recording showed it.</summary>
    public readonly record struct Value(AtkValueType Type, int Number)
    {
        public static Value Int(int number) => new(AtkValueType.Int, number);

        public static Value UInt(uint number) => new(AtkValueType.UInt, (int)number);

        public static Value Bool(bool value) => new(AtkValueType.Bool, value ? 1 : 0);
    }

    /// <param name="Label">What the button is called, for the player when the step is handed over.</param>
    public sealed record Command(string Label, string Addon, IReadOnlyList<Value> Values, bool Closes);

    /// <summary>"Commence Battle", once the familiars are called. Not recorded yet.</summary>
    public static Command? CommenceBattle => null;

    /// <summary>Taking the spoils after a fight. Not recorded yet.</summary>
    public static Command? TakeSpoils => null;

    /// <summary>Confirming who rests at a campsite, after the most hurt are picked. Not recorded yet.</summary>
    public static Command? ConfirmCampsite => null;

    /// <summary>Leaving a shop without buying. Not recorded yet.</summary>
    public static Command? LeaveShop => null;

    /// <summary>Taking an item from a treasure coffer. Not recorded yet.</summary>
    public static Command? TakeTreasure => null;

    /// <summary>Closing the board's result. Not recorded yet.</summary>
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
            switch (value.Type)
            {
                case AtkValueType.UInt:
                    values[i].SetUInt((uint)value.Number);
                    break;

                case AtkValueType.Bool:
                    values[i].SetBool(value.Number != 0);
                    break;

                default:
                    values[i].SetInt(value.Number);
                    break;
            }
        }

        addon->FireCallback((uint)count, values, command.Closes);
        Services.Log.Information($"Sent \"{command.Label}\" to {command.Addon}.");
        return true;
    }
}

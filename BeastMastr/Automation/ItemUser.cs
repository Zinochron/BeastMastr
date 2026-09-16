using System;
using System.Collections.Generic;
using System.Linq;
using BeastMastr.Data;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Automation;

/// <summary>
/// Drinks the Crucible's healing items from the run's HUD, as recorded on 2026-09-16 23:17: the HUD
/// (<c>XBMContentsMainHUD</c>) sends <c>[6, slot, —]</c>, a context menu offers "Use" and "Discard", and
/// <c>[0, index, 0u, —, —]</c> on the menu uses the item. The slot then shows empty.
///
/// The HUD lists ten item slots in blocks of five from value 9: a Bool, a Bool while the slot holds an
/// item (+1), its <c>Item</c> row (+2), its <c>XBMItem</c> row (+3) and its name (+4).
/// </summary>
public static unsafe class ItemUser
{
    public const string Hud = "XBMContentsMainHUD";
    public const string Menu = "ContextMenu";
    private const int FirstSlot = 9;
    private const int SlotStride = 5;
    private const int SlotCount = 10;
    private const int HasItem = 1;
    private const int Row = 3;
    private const int OpenMenuCommand = 6;

    private static readonly TimeSpan MenuTimeout = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan UsedTimeout = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan AfterUse = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan AfterFailure = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The healing items by <c>XBMItem</c> row, with the share of HP they restore: G1–G4 Beast Potion
    /// (76–79) and G1–G3 Crucible Ash (80–82), which heals nearby familiars too.
    /// </summary>
    public static readonly IReadOnlyDictionary<uint, float> Heals = new Dictionary<uint, float>
    {
        [76] = 0.10f, [77] = 0.23f, [78] = 0.36f, [79] = 0.50f,
        [80] = 0.10f, [81] = 0.25f, [82] = 0.40f,
    };

    private static int stage;
    private static int slot;
    private static uint row;
    private static DateTime stageSince;
    private static DateTime nextTry = DateTime.MinValue;

    /// <summary>Whether a use is under way; nothing else should open or close windows meanwhile.</summary>
    public static bool Busy => stage != 0;

    public static string LastResult { get; private set; } = string.Empty;

    /// <summary>The healing items held: slot and row.</summary>
    public static List<(int Slot, uint Row, float Heal)> HealingItems()
    {
        var items = new List<(int, uint, float)>();
        if (!AddonReader.IsOpen(Hud))
            return items;

        var values = AddonReader.Values(Hud);
        for (var i = 0; i < SlotCount; i++)
        {
            var start = FirstSlot + (i * SlotStride);
            if (start + Row >= values.Count || values[start + HasItem].Text != "True" ||
                !uint.TryParse(values[start + Row].Text, out var r) || !Heals.TryGetValue(r, out var heal))
                continue;

            items.Add((i, r, heal));
        }

        return items;
    }

    /// <summary>
    /// Heals when HP is at or below <paramref name="below"/>: in a fight the strongest item, between
    /// fights the smallest one that reaches <paramref name="below"/> again, so the strong ones are kept.
    /// Call every frame; it does one step at a time. Returns true while it is working.
    /// </summary>
    public static bool Tick(float below, bool inFight)
    {
        if (stage != 0)
        {
            Continue();
            return true;
        }

        if (DateTime.Now < nextTry || Services.Objects.LocalPlayer is not { MaxHp: > 0 } player)
            return false;

        var share = (float)player.CurrentHp / player.MaxHp;
        if (share > below || player.IsDead)
            return false;

        var items = HealingItems();
        if (items.Count == 0)
            return false;

        var missing = below - share;
        var pick = inFight
                       ? items.OrderByDescending(item => item.Heal).First()
                       : items.Where(item => item.Heal >= missing).OrderBy(item => item.Heal).FirstOrDefault() is { Row: > 0 } enough
                           ? enough
                           : items.OrderByDescending(item => item.Heal).First();

        if (!Send(Hud, [Value.Int(OpenMenuCommand), Value.Int(pick.Slot), Value.Undefined()], true))
            return false;

        slot = pick.Slot;
        row = pick.Row;
        stage = 1;
        stageSince = DateTime.Now;
        return true;
    }

    private static void Continue()
    {
        var now = DateTime.Now;
        switch (stage)
        {
            case 1:
                if (AddonReader.IsOpen(Menu))
                {
                    var entries = AddonReader.Values(Menu).Where(value => value.Type.EndsWith("String", StringComparison.Ordinal))
                                             .Select(value => value.Text).ToList();
                    var use = entries.IndexOf("Use");
                    if (use < 0)
                    {
                        Finish($"The item menu offered no \"Use\" ({string.Join(", ", entries)}).", false);
                        return;
                    }

                    if (Send(Menu, [Value.Int(0), Value.Int(use), Value.UInt(0), Value.Undefined(), Value.Undefined()], true))
                    {
                        stage = 2;
                        stageSince = now;
                    }

                    return;
                }

                if (now - stageSince > MenuTimeout)
                    Finish("The item menu did not open.", false);

                return;

            case 2:
                if (!HealingItems().Any(item => item.Slot == slot && item.Row == row))
                {
                    Finish($"Used {Name(row)}.", true);
                    return;
                }

                if (now - stageSince > UsedTimeout)
                    Finish($"{Name(row)} was not used.", false);

                return;
        }
    }

    private static void Finish(string result, bool used)
    {
        LastResult = result;
        Services.Log.Information($"Items: {result}");
        stage = 0;
        nextTry = DateTime.Now + (used ? AfterUse : AfterFailure);
    }

    private static string Name(uint itemRow) => itemRow switch
    {
        76 => "G1 Beast Potion", 77 => "G2 Beast Potion", 78 => "G3 Beast Potion", 79 => "G4 Beast Potion",
        80 => "G1 Crucible Ash", 81 => "G2 Crucible Ash", 82 => "G3 Crucible Ash",
        _ => $"item {itemRow}",
    };

    private readonly record struct Value(AtkValueType Type, int Number)
    {
        public static Value Int(int number) => new(AtkValueType.Int, number);

        public static Value UInt(uint number) => new(AtkValueType.UInt, (int)number);

        public static Value Undefined() => new(AtkValueType.Undefined, 0);
    }

    private static bool Send(string addonName, Value[] command, bool close)
    {
        if (!AddonReader.IsOpen(addonName) || !AddonReader.TryGet(addonName, out var addon))
            return false;

        var values = stackalloc AtkValue[command.Length];
        for (var i = 0; i < command.Length; i++)
        {
            values[i] = default;
            switch (command[i].Type)
            {
                case AtkValueType.Int:
                    values[i].SetInt(command[i].Number);
                    break;
                case AtkValueType.UInt:
                    values[i].SetUInt((uint)command[i].Number);
                    break;
                default:
                    values[i].Type = AtkValueType.Undefined;
                    break;
            }
        }

        addon->FireCallback((uint)command.Length, values, close);
        return true;
    }
}

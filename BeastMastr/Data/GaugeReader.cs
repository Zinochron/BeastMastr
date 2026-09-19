using System;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BeastMastr.Data;

/// <summary>
/// The Beastmaster's own resources.
///
/// FFXIVClientStructs has no gauge struct for the job and Dalamud's job gauges stop at the regular
/// jobs, so the bytes are read raw. The layout is BossMod's <c>BeastmasterGauge</c>: counted from the
/// gauge pointer, past its eight-byte header, <c>+0x8</c> the player's TP, <c>+0x9</c> the pet's,
/// <c>+0xA</c> what the pet's last action cost, <c>+0xB</c> which beast is summoned. What each of
/// them does in a fight is for a run recording to show; until then they are carried as numbers.
/// </summary>
public static unsafe class GaugeReader
{
    /// <summary>Beast Tamer, as BossMod and the ClassJob sheet both have it.</summary>
    public const uint BeastmasterJob = 43;

    /// <summary>How many gauge bytes the recorder keeps. The four known ones and room for what is not known.</summary>
    public const int RawLength = 16;

    public sealed record Gauge(byte PlayerTp, byte PetTp, byte LastPetActionTp, byte SummonedBeast);

    /// <summary>The job the player is on, or 0 before there is a player.</summary>
    public static uint PlayerJob => Services.Objects.LocalPlayer?.ClassJob.RowId ?? 0;

    public static bool IsBeastmaster => PlayerJob == BeastmasterJob;

    /// <summary>The gauge's bytes after its header, or an empty array when there is no gauge.</summary>
    public static byte[] Raw()
    {
        var manager = JobGaugeManager.Instance();
        if (manager == null || manager->CurrentGauge == null)
            return [];

        var bytes = new byte[RawLength];
        new ReadOnlySpan<byte>((byte*)manager->CurrentGauge + 8, RawLength).CopyTo(bytes);
        return bytes;
    }

    /// <summary>The gauge, or null when the player is not a Beastmaster and the bytes mean something else.</summary>
    public static Gauge? Read()
    {
        if (!IsBeastmaster)
            return null;

        var raw = Raw();
        return raw.Length < 4 ? null : new Gauge(raw[0], raw[1], raw[2], raw[3]);
    }

    /// <summary>The three beasts the client lists for the fight, as it stores them.</summary>
    public static byte[] Pets()
    {
        var manager = ActionManager.Instance();
        return manager == null ? [] : manager->BeastmasterPets.ToArray();
    }
}

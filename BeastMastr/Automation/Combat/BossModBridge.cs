using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using BeastMastr.Ipc;

namespace BeastMastr.Automation.Combat;

/// <summary>
/// Hands a fight's movement — and, if asked, its combo — to BossMod, and takes it back afterwards.
///
/// BossMod's presets are switched rather than its settings changed: whatever was active before the
/// fight is noted and put back when it ends, when the run stops, and when this plugin unloads. Only one
/// thing may move the character at a time, so vnavmesh is stopped before BossMod is given the job.
///
/// The Crucible has no obstacle map of BossMod's own, and without one its pathfinding does not know
/// where the platforms end. One is generated around the fight as it starts.
/// </summary>
public sealed class BossModBridge
{
    private const float ObstacleRadius = 30f;

    /// <summary>
    /// Raised whenever the presets written change, so the ones already in BossMod are replaced.
    /// 2: casts are never broken to move — a Battlehorn takes a second, and BossMod broke it to dodge.
    /// </summary>
    private const int PresetVersion = 2;

    /// <summary>After a refusal, how long before asking again. Engaging is asked every frame of a fight.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    private readonly Configuration configuration;
    private List<string>? before;
    private DateTime retryAt = DateTime.MinValue;

    public BossModBridge(Configuration configuration) => this.configuration = configuration;

    /// <summary>Whether a fight is currently handed over.</summary>
    public bool Engaged { get; private set; }

    public string Status { get; private set; } = "Not in use.";

    /// <summary>What BossMod will be asked to do, given the setting and whether it is loaded.</summary>
    public BossModRole Role => BossModIpc.IsLoaded ? configuration.BossModRole : BossModRole.Off;

    /// <summary>BossMod moves the character during the fight.</summary>
    public bool Moves => Engaged && Role != BossModRole.Off;

    /// <summary>BossMod presses the combo during the fight.</summary>
    public bool PlaysCombo => Engaged && Role == BossModRole.DodgeAndRotation;

    public bool Engage(Vector3 around)
    {
        if (Engaged)
            return true;

        if (DateTime.Now < retryAt)
            return false;

        retryAt = DateTime.Now + RetryAfter;

        var role = Role;
        if (role == BossModRole.Off)
        {
            Status = BossModIpc.IsLoaded ? "Turned off." : "BossMod is not loaded.";
            return false;
        }

        if (configuration.BossModPresetVersion < PresetVersion && CreatePresets() == PresetsWritten)
            Services.Log.Information($"BossMod's presets are written again, as version {PresetVersion}.");

        var preset = role == BossModRole.DodgeAndRotation ? configuration.BossModFullPreset : configuration.BossModDodgePreset;
        if (BossModIpc.GetPreset(preset) == null && !CreatePreset(role, preset))
        {
            Status = $"BossMod has no preset \"{preset}\" and would not take one: {BossModIpc.LastError}";
            return false;
        }

        before = BossModIpc.GetActiveList() ?? [];
        NavmeshIpc.Stop();
        BossModIpc.GenerateObstacleMap(around, ObstacleRadius);

        if (!BossModIpc.SetActiveList([preset]))
        {
            Status = $"BossMod would not switch to \"{preset}\": {BossModIpc.LastError}";
            return false;
        }

        Engaged = true;
        retryAt = DateTime.MinValue;
        Status = $"BossMod is playing \"{preset}\".";
        Services.Log.Information(Status);
        return true;
    }

    public void Disengage()
    {
        retryAt = DateTime.MinValue;

        if (!Engaged)
            return;

        BossModIpc.SetActiveList(before ?? []);
        Engaged = false;
        Status = $"BossMod is back to {(before is { Count: > 0 } ? string.Join(", ", before) : "no preset")}.";
        Services.Log.Information(Status);
        before = null;
    }

    private const string PresetsWritten = "Both presets are in BossMod.";

    /// <summary>Writes both presets into BossMod, replacing any of the same name.</summary>
    public string CreatePresets()
    {
        var dodge = CreatePreset(BossModRole.DodgeOnly, configuration.BossModDodgePreset);
        var full = CreatePreset(BossModRole.DodgeAndRotation, configuration.BossModFullPreset);
        if (!dodge || !full)
            return $"BossMod refused: {BossModIpc.LastError}";

        configuration.BossModPresetVersion = PresetVersion;
        configuration.Save();
        return PresetsWritten;
    }

    private static bool CreatePreset(BossModRole role, string name)
    {
        var modules = new Dictionary<string, object>
        {
            ["BossMod.Autorotation.MiscAI.NormalMovement"] = new[]
            {
                new Setting("Destination", "Pathfind"),
                new Setting("Range", "MaxRange"),

                // BossMod takes a cast it did not start for one it may slide out of, and moves at once.
                new Setting("Cast", "Greedy"),
            },
        };

        if (role == BossModRole.DodgeAndRotation)
        {
            modules["BossMod.Autorotation.MiscAI.AutoTarget"] = new[] { new Setting("Retarget", "Hostiles") };
            modules["BossMod.Autorotation.xan.BST"] = new[] { new Setting("Targeting", "Auto") };
        }

        var json = JsonSerializer.Serialize(new { Name = name, Modules = modules });
        return BossModIpc.CreatePreset(json, true);
    }

    private sealed record Setting(string Track, string Option);
}

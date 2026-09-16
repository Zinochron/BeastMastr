using System;
using BeastMastr.Data;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace BeastMastr.Automation.Run;

/// <summary>
/// Notices you taking over.
///
/// The rule the whole plugin follows is that nothing it does may fight what you do by hand. For a run
/// that walks and fights on its own that means: the moment you move, jump, target something or press
/// an action, the run lets go — and only carries on once you have left it alone for a while.
///
/// What counts is read from the game's own input layer, by input id rather than by key, so it follows
/// your key bindings and your controller. That is also what keeps it from tripping over the
/// automation: vnavmesh and BossMod move the character below that layer, and automated actions never
/// go through a hotbar, so none of them looks like you.
/// </summary>
public sealed unsafe class ManualInputGuard : IDisposable
{
    private static readonly InputId[] Movement =
    [
        InputId.MOVE_FORE, InputId.MOVE_BACK, InputId.MOVE_LEFT, InputId.MOVE_RIGHT,
        InputId.MOVE_STRIFE_L, InputId.MOVE_STRIFE_R, InputId.MOVE_AND_STEER,
        InputId.AUTORUN_KEY, InputId.AUTORUN_PAD,
    ];

    private static readonly InputId[] Jump = [InputId.JUMP];

    private static readonly InputId[] Targeting = BuildTargeting();

    private readonly Configuration configuration;
    private readonly ActionWatcher actions;
    private DateTime lastHotbarSeen;

    public ManualInputGuard(Configuration configuration, ActionWatcher actions)
    {
        this.configuration = configuration;
        this.actions = actions;
        lastHotbarSeen = actions.LastHotbarPressAt;
        Services.Framework.Update += OnUpdate;
    }

    /// <summary>When you last took over. <see cref="DateTime.MinValue"/> before the first time.</summary>
    public DateTime LastInputAt { get; private set; } = DateTime.MinValue;

    /// <summary>What it was, in words.</summary>
    public string LastInput { get; private set; } = string.Empty;

    public double SecondsSinceInput => (DateTime.Now - LastInputAt).TotalSeconds;

    /// <summary>Whether you are in control right now: an input within the resume delay.</summary>
    public bool Holding => SecondsSinceInput < configuration.ResumeDelaySeconds;

    /// <summary>
    /// Forgets anything before now. Called as a run starts: the key press that started it, or the
    /// walk that happened before, is not taking over.
    /// </summary>
    public void Reset()
    {
        LastInputAt = DateTime.MinValue;
        LastInput = string.Empty;
        lastHotbarSeen = actions.LastHotbarPressAt;
    }

    private void OnUpdate(IFramework framework)
    {
        try
        {
            if (Detect() is { } what)
            {
                LastInputAt = DateTime.Now;
                LastInput = what;
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Reading input failed.");
        }
    }

    private string? Detect()
    {
        // A hotbar press is checked first and unconditionally recorded as seen, so a press made while
        // typing is not reported the moment typing ends.
        var hotbar = actions.LastHotbarPressAt;
        var pressedAction = hotbar != lastHotbarSeen;
        lastHotbarSeen = hotbar;

        if (configuration.CountActionInput && pressedAction)
            return "an action from a hotbar";

        if (configuration.IgnoreMenuInput && Typing())
            return null;

        var input = UIInputData.Instance();
        if (input == null)
            return null;

        if (configuration.CountMovementInput)
        {
            if (AnyDown(input, Movement))
                return "movement";

            if (Services.Gamepad.LeftStick.Length() > configuration.StickDeadzone * 100f)
                return "the left stick";
        }

        if (configuration.CountJumpInput && AnyDown(input, Jump))
            return "jump";

        if (configuration.CountTargetingInput)
        {
            if (AnyDown(input, Targeting))
                return "targeting";

            if (ClickedSomething())
                return "clicking something in the world";
        }

        return null;
    }

    private static bool AnyDown(UIInputData* input, InputId[] ids)
    {
        foreach (var id in ids)
        {
            if (input->IsInputIdDown(id))
                return true;
        }

        return false;
    }

    /// <summary>A text field of the game's has the keyboard, or one of Dalamud's windows does.</summary>
    private static bool Typing()
    {
        var atk = RaptureAtkModule.Instance();
        if (atk != null && atk->IsTextInputActive())
            return true;

        var io = ImGui.GetIO();
        return io.WantTextInput || io.WantCaptureKeyboard;
    }

    /// <summary>A left click with the cursor on an object, and not on one of Dalamud's windows.</summary>
    private static bool ClickedSomething()
    {
        if (!InputManager.IsLeftMouseDown() || ImGui.GetIO().WantCaptureMouse)
            return false;

        var targets = TargetSystem.Instance();
        return targets != null && targets->MouseOverTarget != null;
    }

    private static InputId[] BuildTargeting()
    {
        var ids = new System.Collections.Generic.List<InputId>();
        for (var id = (int)InputId.TARGET_LOCK; id <= (int)InputId.TARGET_CLOSEST_PC; id++)
            ids.Add((InputId)id);

        return [.. ids];
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}

using System;
using BeastMastr.Data;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BeastMastr.Automation;

/// <summary>
/// Puts the Crucible mode back to the one last used, once, as the board window opens.
///
/// The game forgets it. Every visit starts at Standard, and anyone playing a harder mode steps it
/// back up by hand each time — which is exactly the kind of thing worth doing for someone, and
/// exactly the kind of thing that must not be done twice.
///
/// So: once per opening of the window, and only while the mode is not already the remembered one.
/// After that the window is left alone, and whatever is set by hand becomes the new remembered mode.
/// </summary>
public sealed unsafe class DifficultySelector : IDisposable
{
    /// <summary>Frames after the window appears before touching it, so it has drawn its list.</summary>
    private const int FramesBeforeSetting = 20;

    /// <summary>Frames between two presses. The window rebuilds the board on each one.</summary>
    private const int FramesBetweenPresses = 10;

    /// <summary>How long one press is given to show up before it counts as refused.</summary>
    private const int FramesToConfirm = 60;

    /// <summary>
    /// A hard stop. Four modes need three presses, and a wrong first direction costs a few more; a
    /// count that cannot terminate is worse than a mode left where it was.
    /// </summary>
    private const int MostPresses = 8;

    private readonly Configuration configuration;

    private bool windowWasOpen;
    private int countdown = -1;
    private int cooldown;
    private int presses;
    private int framesWaited;

    /// <summary>Where the mode stood when the last press was sent, or -1 when none is outstanding.</summary>
    private int pressedFrom = -1;

    /// <summary>Which of the two buttons is currently believed to raise the mode. Corrected by trying.</summary>
    private bool raising = true;

    private bool acting;

    public DifficultySelector(Configuration configuration)
    {
        this.configuration = configuration;
        Services.Framework.Update += OnUpdate;
    }

    public string Status { get; private set; } = string.Empty;

    private void OnUpdate(IFramework framework)
    {
        if (!AddonReader.IsOpen(XbmColumns.StageDetailList.Addon))
        {
            windowWasOpen = false;
            countdown = -1;
            Stop();
            return;
        }

        // An opening, which is the one moment this may act.
        if (!windowWasOpen)
        {
            windowWasOpen = true;
            countdown = configuration.RememberCrucibleMode ? FramesBeforeSetting : -1;
        }

        if (CrucibleModeReader.Read() is not { } mode || mode.Index < 0)
            return;

        if (acting)
        {
            Continue(mode);
            return;
        }

        // Learned only while this is not the one doing the changing, or it would remember its own
        // steps on the way to the mode it was already aiming for.
        Remember(mode);

        if (countdown >= 0 && --countdown < 0)
            Begin(mode);
    }

    private void Remember(CrucibleModeReader.Mode mode)
    {
        if (configuration.LastCrucibleMode == mode.Index)
            return;

        configuration.LastCrucibleMode = mode.Index;
        configuration.Save();

        Services.Log.Debug($"Crucible mode remembered: {mode.Label} ({mode.Index + 1} of {mode.Options.Count}).");
    }

    private void Begin(CrucibleModeReader.Mode mode)
    {
        var target = configuration.LastCrucibleMode;

        if (target < 0 || target >= mode.Options.Count || target == mode.Index)
        {
            Status = $"Crucible mode is {mode.Label}; nothing to set.";
            return;
        }

        acting = true;
        presses = 0;
        framesWaited = 0;
        pressedFrom = -1;
        cooldown = 0;

        // A first guess only. Which button raises the mode is settled by watching what one press
        // does, not by assuming that the right-hand one counts up.
        raising = target > mode.Index;

        Status = $"Setting the Crucible mode back to {mode.Options[target]}, from {mode.Label}.";
        Services.Log.Information(Status);
    }

    private void Continue(CrucibleModeReader.Mode mode)
    {
        if (cooldown-- > 0)
            return;

        var target = configuration.LastCrucibleMode;

        if (mode.Index == target)
        {
            Status = $"Crucible mode set to {mode.Label}.";
            Services.Log.Information(Status);
            Stop();
            return;
        }

        if (pressedFrom >= 0)
        {
            if (mode.Index == pressedFrom)
            {
                if (++framesWaited < FramesToConfirm)
                {
                    cooldown = 1;
                    return;
                }

                Give($"The board window did not move the Crucible mode off {mode.Label}.");
                return;
            }

            // It moved. Whether that was towards the mode wanted is the only thing that says which
            // button is which, so it is read off the move rather than assumed.
            if (Math.Abs(target - mode.Index) > Math.Abs(target - pressedFrom))
                raising = !raising;

            pressedFrom = -1;
            framesWaited = 0;
        }

        if (++presses > MostPresses)
        {
            Give($"Gave up setting the Crucible mode after {MostPresses} presses; it is on {mode.Label}.");
            return;
        }

        var command = raising
                          ? XbmColumns.StageDetailList.RaiseModeCommand
                          : XbmColumns.StageDetailList.LowerModeCommand;

        if (!Fire(command))
        {
            Give("The board window went away while setting the Crucible mode.");
            return;
        }

        pressedFrom = mode.Index;
        cooldown = FramesBetweenPresses;
    }

    /// <summary>
    /// One of the two stepper buttons, as recorded from real clicks: <c>[5]</c> raises and
    /// <c>[4]</c> lowers, one Int each, sent with the window closing — which is how the game sends
    /// them, and how the board gets rebuilt for the new mode.
    /// </summary>
    private static bool Fire(int command)
    {
        if (!AddonReader.TryGet(XbmColumns.StageDetailList.Addon, out var addon))
            return false;

        var values = stackalloc AtkValue[1];
        values[0].SetInt(command);

        addon->FireCallback(1, values, true);
        return true;
    }

    /// <summary>
    /// Said in chat, unlike the quiet success. A mode left where it was is worth knowing about before
    /// a run starts on it.
    /// </summary>
    private void Give(string why)
    {
        Stop();
        Status = why;
        Services.Log.Warning(why);
        Services.Chat.Print($"[BeastMastr] {why}");
    }

    private void Stop()
    {
        acting = false;
        presses = 0;
        framesWaited = 0;
        pressedFrom = -1;
        cooldown = 0;
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}

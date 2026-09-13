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
/// So: once per opening, and only while the mode is not already the remembered one. After that the
/// window is left alone, and whatever is set by hand becomes the new remembered mode.
///
/// Two things the first version got wrong, both found by it doing nothing at all:
///
/// **The window being open is not an opening.** `XBMStageDetailList` stays loaded and reports itself
/// open long after it was last used — a capture caught it "open" in Central Shroud, nowhere near the
/// Crucible. So the opening this acts on is the **mode block appearing**, which only happens while
/// the window is really up.
///
/// **Learning has to wait its turn.** The window opens on Standard. Learning on every frame meant
/// Standard was remembered as the mode wanted before the setting step ever ran, which then found
/// nothing to do — it overwrote the very thing it was there to restore.
/// </summary>
public sealed unsafe class DifficultySelector : IDisposable
{
    /// <summary>
    /// Frames after the mode appears before touching it. Enough for the window to have drawn, and
    /// no more: the mode being readable at all is already the sign that it is there.
    /// </summary>
    private const int FramesBeforeSetting = 6;

    /// <summary>
    /// Frames before looking at what a press did. Short on purpose — the pacing comes from the
    /// confirmation rather than from here. A press is never followed by another until the mode has
    /// actually moved, so the game sets the speed and this only decides how soon it is asked.
    /// </summary>
    private const int FramesBetweenPresses = 2;

    /// <summary>
    /// How long one press is given to show up before it counts as refused. A limit, not a wait: it
    /// costs nothing when things go well, and a tight one would call a slow frame a failure.
    /// </summary>
    private const int FramesToConfirm = 60;

    /// <summary>
    /// A hard stop. Four modes need three presses, and a wrong first direction costs a few more; a
    /// count that cannot terminate is worse than a mode left where it was.
    /// </summary>
    private const int MostPresses = 8;

    private readonly Configuration configuration;

    /// <summary>Whether the mode block was there last frame. Its arrival is the opening.</summary>
    private bool modeWasThere;

    /// <summary>Whether this opening has had its one chance to set the mode. Nothing is learned before that.</summary>
    private bool settled;

    /// <summary>One log line per opening, rather than one per frame.</summary>
    private bool announced;

    /// <summary>The same, for the case where the window is up but has no mode to offer.</summary>
    private bool announcedMissing;

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
        var windowOpen = AddonReader.IsOpen(XbmColumns.StageDetailList.Addon);
        var mode = windowOpen ? CrucibleModeReader.Read() : null;

        if (mode is null || mode.Index < 0)
        {
            // Mid-press the name shown can briefly be one the list has not drawn a row for yet, and
            // then it has no position. That is a moment to wait through, not a reason to forget what
            // is being done.
            if (acting && mode is not null)
                return;

            // Said once, because "it did nothing" and "it could not read the mode" look identical
            // from the outside and need different fixes.
            if (windowOpen && !announcedMissing)
            {
                announcedMissing = true;
                Status = "The board window offers no Crucible mode to set.";
                Services.Log.Information($"{Status} What the reader saw: {Describe(mode)}");
            }

            if (!windowOpen)
                announcedMissing = false;

            modeWasThere = false;
            settled = false;
            announced = false;
            countdown = -1;
            Stop();
            return;
        }

        announcedMissing = false;

        if (!modeWasThere)
        {
            modeWasThere = true;
            settled = !configuration.RememberCrucibleMode;
            countdown = configuration.RememberCrucibleMode ? FramesBeforeSetting : -1;
        }

        if (!announced)
        {
            announced = true;
            Services.Log.Information(
                $"Board window opened on {mode.Label}, {mode.Index + 1} of {mode.Count}; " +
                $"remembered {(configuration.LastCrucibleMode < 0 ? "nothing" : $"{configuration.LastCrucibleMode + 1}")}. " +
                $"Drawn so far: [{string.Join(", ", mode.Options)}], list says {mode.SelectedItemIndex}.");
        }

        if (acting)
        {
            Continue(mode);
            return;
        }

        // Only after this opening has had its turn. Learning first would take the Standard the
        // window opens on for the mode wanted.
        if (settled)
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

        Services.Log.Information($"Crucible mode remembered: {mode.Label}, " +
                                 $"{mode.Index + 1} of {mode.Count}.");
    }

    private void Begin(CrucibleModeReader.Mode mode)
    {
        var target = configuration.LastCrucibleMode;

        // Range-checked against the list's own length, never against the names drawn so far. The
        // first version checked the drawn ones, which on a freshly opened window is the single mode
        // it is set to — so every higher mode looked out of range, and it declared there was nothing
        // to do and then learned Standard over the mode it was meant to restore.
        if (target < 0 || (mode.Count > 0 && target >= mode.Count) || target == mode.Index)
        {
            settled = true;
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

        // Named where the name has been drawn, numbered where it has not — a mode never yet shown
        // has no name to give, and "mode 4" beats an index nobody asked to see.
        var wanted = target < mode.Options.Count ? mode.Options[target] : $"mode {target + 1}";

        Status = $"Setting the Crucible mode back to {wanted}, from {mode.Label}.";
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
    /// One of the two stepper buttons, as recorded from real clicks: <c>[5]</c> and <c>[4]</c>, one
    /// Int each, sent with the window closing — which is how the game sends them, and how the board
    /// gets rebuilt for the new mode.
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

    /// <summary>What the reader saw, for the log line when it saw nothing usable.</summary>
    private static string Describe(CrucibleModeReader.Mode? mode) =>
        mode is null
            ? "no drop-down in the board window"
            : $"label \"{mode.Label}\", not among [{string.Join(", ", mode.Options)}]";

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
        if (modeWasThere)
            settled = true;

        acting = false;
        presses = 0;
        framesWaited = 0;
        pressedFrom = -1;
        cooldown = 0;
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}

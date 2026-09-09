using System;
using System.Text;
using Dalamud.Plugin.Services;

namespace BeastMastr.Data;

/// <summary>
/// A sweep that fires a few seconds after you ask for it.
///
/// Some Beastmaster windows only exist while the cursor rests on something — an enemy on the board,
/// a shop line, an item — and they close the moment you reach for a button. Those windows cannot be
/// captured by pressing anything. So: arm this, put the cursor where the window appears, and let it
/// fire on its own while the window is still up.
/// </summary>
public sealed class DelayedSweep : IDisposable
{
    private DateTime fireAt = DateTime.MinValue;

    public DelayedSweep()
    {
        Services.Framework.Update += OnUpdate;
    }

    /// <summary>Seconds left, or null when nothing is armed. Drives the countdown in the UI.</summary>
    public double? SecondsRemaining =>
        fireAt == DateTime.MinValue ? null : Math.Max(0, (fireAt - DateTime.Now).TotalSeconds);

    /// <summary>Where the last capture landed, for the UI to show.</summary>
    public string LastPath { get; private set; } = string.Empty;

    public void Arm(int seconds)
    {
        fireAt = DateTime.Now.AddSeconds(seconds);
        LastPath = string.Empty;
    }

    public void Cancel() => fireAt = DateTime.MinValue;

    private void OnUpdate(IFramework framework)
    {
        if (fireAt == DateTime.MinValue || DateTime.Now < fireAt)
            return;

        fireAt = DateTime.MinValue;
        LastPath = Capture() ?? "could not be written — see the log";
    }

    /// <summary>
    /// Every Beastmaster window that is open at this instant. Hover windows are the reason this
    /// exists, but sweeping all of them keeps the surrounding state consistent in the same file.
    /// </summary>
    public static string? Capture()
    {
        var text = new StringBuilder();
        text.AppendLine($"# BeastMastr sweep {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine();

        // Asked of the game rather than taken from the hand written list, so windows nobody has
        // named yet — the shop, the hover panels — are swept too.
        foreach (var addon in AddonReader.OpenAddonNames())
        {
            text.AppendLine($"<!-- {BeastmasterData.NoteFor(addon)} -->");
            text.AppendLine(AddonReader.ToText(addon, AddonReader.Values(addon)));
            text.AppendLine(AddonReader.ToText(addon, AddonReader.Nodes(addon)));
            text.AppendLine();
        }

        return CaptureStore.Save("sweep", text.ToString());
    }

    public void Dispose() => Services.Framework.Update -= OnUpdate;
}

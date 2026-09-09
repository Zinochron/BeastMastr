using System;
using System.IO;

namespace BeastMastr.Data;

/// <summary>
/// Writes a dump to a file instead of the clipboard.
///
/// Captures are read by whoever is doing the mapping work, and a node tree is hundreds of lines —
/// far past what is comfortable to move through a clipboard. Writing them next to the plugin's
/// config means a capture is one button here and a file read there.
/// </summary>
public static class CaptureStore
{
    public static DirectoryInfo Directory
    {
        get
        {
            var directory = new DirectoryInfo(
                Path.Combine(Services.PluginInterface.ConfigDirectory.FullName, "captures"));

            directory.Create();
            return directory;
        }
    }

    /// <summary>
    /// Writes <paramref name="content"/> and returns the path, or null when it could not be written.
    /// Names carry a timestamp so a second capture of the same window never silently replaces the
    /// first — comparing two captures is the whole technique for spotting a recycled list.
    /// </summary>
    public static string? Save(string name, string content)
    {
        try
        {
            var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(Directory.FullName,
                                    $"{safe}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

            File.WriteAllText(path, content);
            Services.Log.Information($"Capture written to {path}");
            return path;
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, $"Could not write the capture \"{name}\".");
            return null;
        }
    }
}

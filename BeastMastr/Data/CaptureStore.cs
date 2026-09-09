using System;
using System.IO;

namespace BeastMastr.Data;

/// <summary>
/// Writes a dump to a file instead of the clipboard.
///
/// Captures are read by whoever is doing the mapping work, and a node tree runs to hundreds of
/// lines — far past what is comfortable to move through a clipboard.
/// </summary>
public static class CaptureStore
{
    /// <summary>
    /// <c>captures/</c> in the repository when running as a dev plugin, so a capture lands where the
    /// person reading it is already working. Falls back to the plugin's config directory for an
    /// installed copy, which has no repository to write into.
    /// </summary>
    public static DirectoryInfo Directory
    {
        get
        {
            var root = RepositoryRoot() ?? Services.PluginInterface.ConfigDirectory;
            var directory = new DirectoryInfo(Path.Combine(root.FullName, "captures"));

            directory.Create();
            return directory;
        }
    }

    /// <summary>
    /// Walks up from the loaded assembly looking for the solution file. A dev plugin runs out of
    /// <c>bin/x64/Debug</c> inside the working copy, so the repository is a few levels above it;
    /// an installed plugin sits somewhere else entirely and finds nothing, which is the signal to
    /// fall back.
    /// </summary>
    private static DirectoryInfo? RepositoryRoot()
    {
        try
        {
            var directory = Services.PluginInterface.AssemblyLocation.Directory;

            for (var depth = 0; directory != null && depth < 8; depth++, directory = directory.Parent)
            {
                if (directory.GetFiles("BeastMastr.slnx").Length > 0)
                    return directory;
            }
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, "Could not locate the repository; captures go to the config directory.");
        }

        return null;
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

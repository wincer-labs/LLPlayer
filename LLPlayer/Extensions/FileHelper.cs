using System.IO;
using static FlyleafLib.Utils;

namespace LLPlayer.Extensions;

public static class FileHelper
{
    private static readonly HashSet<string> MediaExtensions = new(
        ExtensionsVideo.Concat(ExtensionsAudio), StringComparer.OrdinalIgnoreCase);

    public static bool IsLocalMedia(string? path) => !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path) && MediaExtensions.Contains(Path.GetExtension(path).TrimStart('.'));

    /// <summary>Audio and video in this directory only, in natural filename order.</summary>
    public static List<string> GetDirectoryMedia(string filePath)
    {
        string directory = Path.GetDirectoryName(filePath)
            ?? throw new ArgumentException("A full file path is required", nameof(filePath));
        return Directory.EnumerateFiles(directory)
            .Where(IsLocalMedia)
            .OrderBy(Path.GetFileName, new NaturalStringComparer())
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Retrieves the next and previous file from the specified file path.
    /// Select audio/video files in the same folder, sorted in natural alphabetical order.
    /// Returns null if the next or previous file does not exist.
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public static (string? prev, string? next) GetNextAndPreviousFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("file does not exist", filePath);
        }

        List<string> foundFiles = GetDirectoryMedia(filePath);

        int currentIndex = foundFiles.FindIndex(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
        if (currentIndex == -1)
        {
            throw new InvalidOperationException($"current file does not exist: {filePath}");
        }

        string? next = (currentIndex < foundFiles.Count - 1) ? foundFiles[currentIndex + 1] : null;
        string? prev = (currentIndex > 0) ? foundFiles[currentIndex - 1] : null;

        return (prev, next);
    }
}

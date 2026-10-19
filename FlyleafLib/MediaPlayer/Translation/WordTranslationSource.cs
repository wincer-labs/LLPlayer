using System.IO;

namespace FlyleafLib.MediaPlayer.Translation;

#nullable enable

/// <summary>Public media labels for local lookup integrations, without the original disk path.</summary>
public sealed record WordTranslationSource(string Category, string? MediaTitle, long? PositionMs)
{
    public static WordTranslationSource FromMedia(string? media, long? positionMs = null)
    {
        WordTranslationSource fallback = new("LLPlayer", null, positionMs);
        if (string.IsNullOrWhiteSpace(media))
            return fallback;

        if (Uri.TryCreate(media, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile)
                return fallback;
            media = uri.LocalPath;
        }

        if (!Path.IsPathFullyQualified(media))
            return fallback;

        try
        {
            string fullPath = Path.GetFullPath(media);
            string? directory = Path.GetDirectoryName(fullPath);
            string? category = directory == null ? null : Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
            string title = Path.GetFileName(fullPath);
            return new(string.IsNullOrWhiteSpace(category) ? "LLPlayer" : category,
                string.IsNullOrWhiteSpace(title) ? null : title, positionMs);
        }
        catch (ArgumentException)
        {
            return fallback;
        }
    }
}

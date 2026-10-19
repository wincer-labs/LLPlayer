using System.IO;
using System.Text.Json;

namespace LLPlayer.Services;

public sealed record PlaybackState(string FilePath, long PositionTicks, long DurationTicks)
{
    public long ResumePosition(long durationTicks) =>
        PositionTicks < 0 || durationTicks <= 0 || PositionTicks >= durationTicks ? 0 : PositionTicks;
}

/// <summary>Separate from settings so playback never rewrites user configuration.</summary>
public sealed class PlaybackStateStore(string path)
{
    public PlaybackState? Load()
    {
        if (!File.Exists(path))
            return null;
        var state = JsonSerializer.Deserialize<PlaybackState>(File.ReadAllText(path));
        return state != null && !string.IsNullOrWhiteSpace(state.FilePath) && Path.IsPathFullyQualified(state.FilePath)
            ? state : null;
    }

    public void Save(PlaybackState state)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}

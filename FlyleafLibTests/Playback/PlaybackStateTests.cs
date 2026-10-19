using System.IO;
using System.Text.Json;
using LLPlayer.Extensions;
using LLPlayer.Services;

namespace FlyleafLib.Playback;

public class PlaybackStateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LLPlayer-playback-" + Guid.NewGuid());
    public PlaybackStateTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void AtomicallyReplacesHistoryAndLeavesNoTemporaryFiles()
    {
        var store = new PlaybackStateStore(Path.Combine(_directory, "history.json"));
        Assert.Null(store.Load());
        var first = new PlaybackState(Path.Combine(_directory, "1.mp4"), 123, 456);
        store.Save(first);
        Assert.Equal(first, store.Load());
        var second = first with { PositionTicks = 321 };
        store.Save(second);
        Assert.Equal(second, store.Load());
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void RejectsInvalidHistoryPathsAndReportsMalformedJson()
    {
        string path = Path.Combine(_directory, "history.json");
        var store = new PlaybackStateStore(path);
        File.WriteAllText(path, "{broken");
        Assert.Throws<JsonException>(() => store.Load());
        store.Save(new PlaybackState("relative.mp4", 0, 10));
        Assert.Null(store.Load());
        File.WriteAllText(path, "null");
        Assert.Null(store.Load());
    }

    [Theory]
    [InlineData(-1, 100, 0)]
    [InlineData(0, 100, 0)]
    [InlineData(50, 100, 50)]
    [InlineData(100, 100, 0)]
    [InlineData(200, 100, 0)]
    [InlineData(50, 0, 0)]
    public void ValidatesPositionAgainstCurrentDuration(long position, long duration, long expected)
        => Assert.Equal(expected, new PlaybackState("unused", position, duration).ResumePosition(duration));

    [Fact]
    public void DirectoryListMixesAudioVideoNaturallyAndDoesNotRecurse()
    {
        foreach (string name in new[] { "Episode10.MKV", "Episode2.mp4", "Episode1.WAV", "Episode2.srt", "cover.png", "list.m3u" })
            File.WriteAllText(Path.Combine(_directory, name), "");
        Directory.CreateDirectory(Path.Combine(_directory, "nested"));
        File.WriteAllText(Path.Combine(_directory, "nested", "Episode3.mp4"), "");
        string first = Path.Combine(_directory, "Episode1.WAV");
        string middle = Path.Combine(_directory, "Episode2.mp4");
        string last = Path.Combine(_directory, "Episode10.MKV");
        Assert.Equal(new[] { first, middle, last }, FileHelper.GetDirectoryMedia(first));
        Assert.Equal((null, middle), FileHelper.GetNextAndPreviousFile(first));
        Assert.Equal((first, last), FileHelper.GetNextAndPreviousFile(middle));
        Assert.Equal((middle, null), FileHelper.GetNextAndPreviousFile(last));
        File.Delete(middle);
        Assert.Equal(new[] { first, last }, FileHelper.GetDirectoryMedia(first));
        Assert.Throws<FileNotFoundException>(() => FileHelper.GetNextAndPreviousFile(middle));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using FlyleafLib.MediaPlayer;
using LLPlayer.Services;

namespace FlyleafLib.Playback;

// Run on the existing desktop fixture's STA, sharing its Engine/Application lifetime.
internal static class PlaybackIntegrationScenarios
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "LLPlayer-resume-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string first = Path.Combine(directory, "Episode2.wav");
            string second = Path.Combine(directory, "Episode10.wav");
            WriteWave(first);
            File.Copy(first, second);
            var store = new PlaybackStateStore(Path.Combine(directory, "history.json"));
            using (var player = NewPlayer())
            using (var playback = new PlaybackController(player, store))
            {
                Assert.False(playback.HasHistory);
                var opened = player.Open(first);
                Assert.True(opened.Success, opened.Error);
                PumpUntil(() => playback.Count == 2);
                player.SeekAccurate(5000);
                PumpUntil(() => player.CurTime == TimeSpan.FromSeconds(5).Ticks);
                playback.Save();
                Assert.Equal(first, store.Load()!.FilePath);
                Assert.Equal(TimeSpan.FromSeconds(5).Ticks, store.Load()!.PositionTicks);
                Assert.Null(playback.PreviousItem);
                Assert.Equal(second, Assert.IsType<DirectoryPlaylistItem>(playback.NextItem).FilePath);
                Assert.Single(player.Playlist.Items); // Directory files never enter decoder playlist.
            }

            using (var player = NewPlayer())
            using (var playback = new PlaybackController(player, store))
            {
                Assert.True(playback.HasHistory);
                Pump();
                Assert.False(player.CanPlay); // Remains idle until Play.
                player.TogglePlayPause();
                player.TogglePlayPause(); // Repeated requests cannot enqueue another restore.
                PumpUntil(() => player.CanPlay && player.IsPlaying && player.CurTime >= TimeSpan.FromSeconds(5).Ticks);
                player.Pause();
                Assert.Equal(first, player.Playlist.Url);
                PumpUntil(() => playback.Count == 2);
                playback.OpenItem(playback.NextItem);
                PumpUntil(() => player.Playlist.Url == second && player.CanPlay && !player.IsOpening);
                PumpUntil(() => playback.PreviousItem is DirectoryPlaylistItem);
                Assert.False(player.IsPlaying); // Normal opens still honor AutoPlay=false.
                Assert.True(Assert.IsType<DirectoryPlaylistItem>(playback.Items.Cast<object>().Last()).Enabled);
                Assert.Single(player.Playlist.Items);
                playback.Save();
                Assert.Equal(second, store.Load()!.FilePath);
            }

            using (var player = NewPlayer())
            using (var playback = new PlaybackController(player, store))
            {
                player.Play(); // Host callback is queued.
                var opened = player.Open(first); // Explicit input wins before callback runs.
                Assert.True(opened.Success, opened.Error);
                PumpUntil(() => playback.Count == 2);
                Assert.Equal(first, player.Playlist.Url);
                Assert.False(player.IsPlaying);
                Assert.True(player.CurTime < TimeSpan.FromSeconds(1).Ticks);
            }

            string list = Path.Combine(directory, "list.m3u");
            File.WriteAllText(list, $"#EXTM3U\n#EXTINF:20,First\n{first}\n#EXTINF:20,Second\n{second}\n");
            using (var player = NewPlayer())
            using (var playback = new PlaybackController(player, store))
            {
                var opened = player.Open(list);
                Assert.True(opened.Success, opened.Error);
                PumpUntil(() => player.Playlist.Items.Count == 2);
                Assert.Same(player.Playlist.Items, playback.Items);
                Assert.Same(player.Playlist.NextItem, playback.NextItem);
            }

            File.WriteAllText(Path.Combine(directory, "history.json"), "{invalid");
            using (var player = NewPlayer())
            using (var playback = new PlaybackController(player, store))
                Assert.False(playback.HasHistory); // Corruption cannot prevent startup.
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static Player NewPlayer()
    {
        var config = new Config();
        config.Player.AutoPlay = false;
        return new Player(config);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void PumpUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            Pump();
            if (condition()) return;
            Thread.Sleep(10);
        } while (watch.Elapsed < TimeSpan.FromSeconds(8));
        Assert.True(condition(), "Playback condition timed out");
    }

    private static void WriteWave(string path)
    {
        const int samples = 16000 * 20;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000);
        writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(samples * 2); writer.Write(new byte[samples * 2]);
    }
}

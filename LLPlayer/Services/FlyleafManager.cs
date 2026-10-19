using System.IO;
using System.Windows;
using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace LLPlayer.Services;

public class FlyleafManager
{
    public Player Player { get; }
    public Config PlayerConfig => Player.Config;
    public FlyleafHost? FlyleafHost => Player.Host as FlyleafHost;
    public AppConfig Config { get; }
    public AppActions Action { get; }
    public PlaybackController Playback { get; }

    public AudioEngine AudioEngine => Engine.Audio;
    public EngineConfig ConfigEngine => Engine.Config;

    public FlyleafManager(Player player, IDialogService dialogService)
    {
        Player = player;

        // Load app configuration at this time
        Config = LoadAppConfig();
        Playback = new PlaybackController(Player, new PlaybackStateStore(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LLPlayer.Playback.json")));
        Action = new AppActions(Player, Config, dialogService, Playback);
    }

    private AppConfig LoadAppConfig()
    {
        AppConfig? config = null;

        if (File.Exists(App.AppConfigPath))
        {
            try
            {
                config = AppConfig.Load(App.AppConfigPath);

                if (config.Version != App.Version)
                {
                    config.Version = App.Version;
                    config.Save(App.AppConfigPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot load AppConfig from {Path.GetFileName(App.AppConfigPath)}, Please review the settings or delete the config file. Error details are recorded in {Path.GetFileName(App.CrashLogPath)}.");
                try
                {
                    File.WriteAllText(App.CrashLogPath, "AppConfig Loading Error: " + ex);
                }
                catch
                {
                    // ignored
                }

                Application.Current.Shutdown();
            }
        }

        if (config == null)
        {
            config = new AppConfig();
        }
        config.Initialize(this);

        return config;
    }
}

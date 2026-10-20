using System.Runtime.ExceptionServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FlyleafLib.Controls.WPF;
using static FlyleafLib.Utils.NativeMethods;

namespace FlyleafLib.Controls;

[CollectionDefinition("Desktop UI", DisableParallelization = true)]
public class DesktopUiCollection;

[Collection("Desktop UI")]
public class OwnerFullScreenTests
{
    [Fact]
    public void KeepsSidebarAttachedAndRestoresWindow()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                Utils.IsTesting = false;
                Utils.SaveOriginalCulture();
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                while (root != null && !File.Exists(Path.Combine(root.FullName, "LLPlayer.slnx")))
                    root = root.Parent;
                Assert.NotNull(root);
                Engine.Start(new EngineConfig { FFmpegPath = Path.Combine(root.FullName, "FFmpeg"), UIRefresh = false });
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                RunScenario(false);
                RunScenario(true);
                MouseActivityScenarios.Run();
                Playback.PlaybackIntegrationScenarios.Run();
            }
            catch (Exception error) { failure = error; }
            finally
            {
                Application.Current?.Shutdown();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
                Utils.IsTesting = true;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Desktop playback/fullscreen test timed out");
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void RunScenario(bool initiallyMaximized)
    {
        Window window = new() { Width = 900, Height = 600, Left = 80, Top = 80, ShowInTaskbar = false };
        using FlyleafHost host = new() { FullScreenWithOwner = true, KeyBindings = AvailableWindows.None };
        try
        {
            Grid layout = new();
            layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var sidebarColumn = new ColumnDefinition { Width = new GridLength(240) };
            layout.ColumnDefinitions.Add(sidebarColumn);
            Border sidebar = new() { Child = new TextBox { Text = "Subtitle search" } };
            Grid.SetColumn(sidebar, 1);
            layout.Children.Add(host);
            layout.Children.Add(sidebar);
            window.Content = layout;
            window.Show();
            Pump(window);
            if (initiallyMaximized)
            {
                window.WindowState = WindowState.Maximized;
                Pump(window);
            }
            Rect restore = window.RestoreBounds;
            var priorState = window.WindowState;
            var monitor = System.Windows.Forms.Screen.FromHandle(host.OwnerHandle).Bounds;

            for (int cycle = 0; cycle < 2; cycle++)
            {
                host.IsFullScreen = true;
                Pump(window);
                Assert.True(host.IsOwnerFullScreen);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                RECT bounds = new();
                GetWindowRect(host.OwnerHandle, ref bounds);
                Assert.Equal(monitor.Left, bounds.Left);
                Assert.Equal(monitor.Top, bounds.Top);
                Assert.Equal(monitor.Right, bounds.Right);
                Assert.Equal(monitor.Bottom, bounds.Bottom);
                Assert.Equal(240, sidebar.ActualWidth, 1);
                Assert.True(host.ActualWidth < window.ActualWidth);

                double videoWidth = host.Surface.ActualWidth;
                sidebarColumn.Width = new GridLength(0);
                Pump(window);
                Assert.True(host.Surface.ActualWidth > videoWidth);
                sidebarColumn.Width = new GridLength(240);
                Pump(window);

                host.IsMinimized = true;
                Pump(window);
                Assert.Equal(WindowState.Minimized, window.WindowState);
                host.IsMinimized = false;
                Pump(window);
                Assert.True(host.IsFullScreen);
                Assert.True(host.IsOwnerFullScreen);

                host.IsFullScreen = false;
                Pump(window);
                Assert.False(host.IsOwnerFullScreen);
                Assert.Equal(priorState, window.WindowState);
                Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
                Assert.Equal(restore.Width, window.RestoreBounds.Width, 1);
                Assert.Equal(restore.Height, window.RestoreBounds.Height, 1);
                Assert.Equal(restore.Left, window.RestoreBounds.Left, 1);
                Assert.Equal(restore.Top, window.RestoreBounds.Top, 1);
            }
        }
        finally { window.Close(); }
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}

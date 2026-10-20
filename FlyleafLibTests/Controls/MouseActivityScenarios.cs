using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;
using static FlyleafLib.Utils.NativeMethods;

namespace FlyleafLib.Controls;

// Share the desktop fixture's STA and Application/Engine lifetime.
internal static class MouseActivityScenarios
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        using Player player = new(new Config());
        using FlyleafHost host = new() { Player = player, ActivityTimeout = 1000, FullScreenWithOwner = true };
        Border overlay = new() { Background = Brushes.Transparent };
        host.Content = overlay;
        Window owner = new() { Width = 600, Height = 400, ShowInTaskbar = false, Content = host };
        var mouseWatch = (Stopwatch)typeof(FlyleafLib.MediaPlayer.Activity).GetField("swMouse", PrivateInstance)!.GetValue(player.Activity)!;
        try
        {
            owner.Show();
            Pump(owner);
            foreach (bool fullScreen in new[] { false, true, false })
            {
                host.IsFullScreen = fullScreen;
                Pump(owner);
                // Changes that retain one window's binding must not unsubscribe its handlers.
                foreach (var bindings in new[] { AvailableWindows.Both, AvailableWindows.Overlay,
                    AvailableWindows.Both, AvailableWindows.Surface, AvailableWindows.Both,
                    AvailableWindows.None, AvailableWindows.Both })
                {
                    host.MouseBindings = bindings;
                    AssertMouseActivity(host.Surface, bindings is AvailableWindows.Surface or AvailableWindows.Both);
                    AssertMouseActivity(overlay, bindings is AvailableWindows.Overlay or AvailableWindows.Both);
                }

                AssertOwnerActivity(new Point(host.ActualWidth / 2, host.ActualHeight - 1), expected: true);
                AssertOwnerActivity(new Point(host.ActualWidth + 10, host.ActualHeight - 1), expected: false);
                AssertOwnerActivity(new Point(-1, 10), expected: false);
                host.MouseBindings = AvailableWindows.None;
                AssertOwnerActivity(new Point(10, 10), expected: false);
                host.MouseBindings = AvailableWindows.Both;
            }

            host.IsAttached = false;
            Pump(owner);
            AssertOwnerActivity(new Point(10, 10), expected: false);
            host.Dispose();
            // The owner remains usable after a hosted player is disposed.
            owner.RaiseEvent(NewMouseMove());
        }
        finally { owner.Close(); }

        void ResetActivity()
        {
            player.Activity.ForceIdle();
            Pump(owner);
            mouseWatch.Reset(); // Engine polling is disabled in this desktop fixture.
            // These routed events represent distinct physical moves without moving the user's cursor.
            typeof(FlyleafHost).GetField("pMM", PrivateInstance)!.SetValue(host,
                new POINT { X = int.MinValue, Y = int.MinValue });
        }

        void AssertMouseActivity(UIElement source, bool expected)
        {
            ResetActivity();
            source.RaiseEvent(NewMouseMove());
            Assert.Equal(expected, mouseWatch.IsRunning);
        }

        void AssertOwnerActivity(Point position, bool expected)
        {
            ResetActivity();
            // Native transparency determines which HWND receives the move; check the
            // owner's video-area routing independently of the physical cursor position.
            typeof(FlyleafHost).GetMethod("RefreshOwnerMouseActivity", PrivateInstance)!
                .Invoke(host, [position]);
            Assert.Equal(expected, mouseWatch.IsRunning);
        }
    }

    private static MouseEventArgs NewMouseMove() => new(Mouse.PrimaryDevice, Environment.TickCount)
    {
        RoutedEvent = Mouse.PreviewMouseMoveEvent,
        Handled = true // Text selection, sliders and other controls can consume mouse input.
    };

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}

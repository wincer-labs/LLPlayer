using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Extensions;
using static FlyleafLib.MediaFramework.MediaContext.DecoderContext;
using OpenCompletedArgs = FlyleafLib.MediaPlayer.OpenCompletedArgs;

namespace LLPlayer.Services;

public sealed record DirectoryPlaylistItem(string FilePath, bool Enabled)
{
    public string Title => Path.GetFileName(FilePath);
}

/// <summary>Application playback history and directory navigation; never mutates decoder playlists.</summary>
public sealed class PlaybackController : Bindable, IDisposable
{
    private readonly Player _player;
    private readonly PlaybackStateStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _saveTimer;
    private readonly LogHandler _log = new("[App] [Playback      ] ");
    private PlaybackState? _lastState;
    private PlaybackState? _restore;
    private int _restoreIssuingThread;
    private string? _activePath;
    private string? _latestUrl;
    private int _openVersion;
    private List<DirectoryPlaylistItem>? _directoryItems;
    private string? _directoryPath;
    private int _scanVersion;
    private bool _disposed;

    public PlaybackController(Player player, PlaybackStateStore store)
    {
        _player = player;
        _store = store;
        _dispatcher = Dispatcher.CurrentDispatcher;
        try { _lastState = store.Load(); }
        catch (Exception ex) { _log.Warn($"Cannot load playback history: {ex.Message}"); }
        _player.PlayRequestedWithoutMedia += OnPlayWithoutMedia;
        _player.Opening += OnOpening;
        _player.OpenCompleted += OnOpenCompleted;
        _player.OpenSessionCompleted += OnSessionCompleted;
        _player.OpenPlaylistItemCompleted += OnPlaylistItemCompleted;
        _player.Playlist.PropertyChanged += OnPlaylistChanged;
        _player.Playlist.Items.CollectionChanged += OnPlaylistItemsChanged;
        _saveTimer = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Background,
            (_, _) => Save(), _dispatcher);
    }

    public bool HasHistory => _lastState != null;
    private bool UsesDirectory => _directoryItems != null && _player.Playlist.Items.Count == 1
        && SamePath(_directoryPath, _player.Playlist.Url);
    public IEnumerable Items => UsesDirectory ? _directoryItems! : _player.Playlist.Items;
    public int Count => UsesDirectory ? _directoryItems!.Count : _player.Playlist.Items.Count;
    public int ExpectedCount => UsesDirectory ? Count : _player.Playlist.ExpectingItems;
    private int CurrentIndex => _directoryItems?.FindIndex(item => item.Enabled) ?? -1;
    public object? PreviousItem => UsesDirectory
        ? CurrentIndex > 0 ? _directoryItems![CurrentIndex - 1] : null : _player.Playlist.PrevItem;
    public object? NextItem => UsesDirectory
        ? CurrentIndex >= 0 && CurrentIndex + 1 < Count ? _directoryItems![CurrentIndex + 1] : null : _player.Playlist.NextItem;
    public string? DirectoryError { get; private set; }
    public DelegateCommand<object> CmdOpenItem => field ??= new(OpenItem);

    public void OpenItem(object? item)
    {
        if (_disposed || item == null)
            return;
        if (item is DirectoryPlaylistItem local)
        {
            if (File.Exists(local.FilePath))
                _player.OpenAsync(local.FilePath);
            else
            {
                MessageBox.Show($"File no longer exists: {local.FilePath}", App.Name);
                _ = RefreshDirectoryAsync();
            }
        }
        else
            _player.Commands.Reopen.Execute(item);
    }

    public async void OpenAdjacent(bool next)
    {
        string? path = _player.Playlist.Url;
        await RefreshDirectoryAsync();
        if (!_disposed && SamePath(path, _player.Playlist.Url))
            OpenItem(next ? NextItem : PreviousItem);
    }

    private void OnPlayWithoutMedia(object? sender, EventArgs args)
    {
        int version = Volatile.Read(ref _openVersion);
        Post(() =>
        {
            if (version != Volatile.Read(ref _openVersion) || _player.CanPlay || _player.IsOpening || _restore != null)
                return;
            if (_lastState == null)
            {
                _player.OpenFromFileDialog();
                return;
            }
            if (!File.Exists(_lastState.FilePath))
            {
                MessageBox.Show($"Last played file no longer exists: {_lastState.FilePath}", App.Name);
                _player.OpenFromFileDialog();
                return;
            }
            _restore = _lastState;
            // Opening is raised synchronously by OpenAsync, including for manual inputs.
            _restoreIssuingThread = Environment.CurrentManagedThreadId;
            try { _player.OpenAsync(_lastState.FilePath); }
            finally { _restoreIssuingThread = 0; }
        });
    }

    private void OnOpening(object? sender, OpeningArgs args)
    {
        if (args.IsSubtitles)
            return;
        Interlocked.Increment(ref _openVersion);
        _latestUrl = Utils.FixFileUrl(args.Url);
        if (_restoreIssuingThread != Environment.CurrentManagedThreadId)
            Interlocked.Exchange(ref _restore, null); // Manual input always wins over history.
        Post(NotifyPlaylist);
    }

    private void OnOpenCompleted(object? sender, OpenCompletedArgs args)
    {
        if (args.IsSubtitles)
            return;
        Post(() =>
        {
            if (!SamePath(Utils.FixFileUrl(args.Url), _latestUrl) || !SamePath(Utils.FixFileUrl(args.Url), _player.Playlist.Url))
                return;
            var restore = Interlocked.Exchange(ref _restore, null);
            if (!args.Success || !_player.CanPlay)
            {
                _activePath = null;
                return;
            }
            if (restore != null && SamePath(args.Url, restore.FilePath))
            {
                long position = restore.ResumePosition(_player.Duration);
                if (position > 0)
                    _player.SeekAccurate((int)Math.Min(position / TimeSpan.TicksPerMillisecond, int.MaxValue));
                _player.Play(); // A deliberate Play request also overrides AutoPlay=false.
            }
            MediaOpened();
        });
    }

    private void OnSessionCompleted(object? sender, OpenSessionCompletedArgs args) => Post(() =>
    {
        if (args.Success)
        {
            _latestUrl = _player.Playlist.Url;
            MediaOpened();
        }
    });
    private void OnPlaylistItemCompleted(object? sender, OpenPlaylistItemCompletedArgs args) => Post(() =>
    {
        if (args.Success) MediaOpened();
    });

    private void MediaOpened()
    {
        _activePath = _player.Playlist.Items.Count == 1 && FileHelper.IsLocalMedia(_player.Playlist.Url)
            ? _player.Playlist.Url : null;
        Save();
        _ = RefreshDirectoryAsync();
    }

    public async Task RefreshDirectoryAsync()
    {
        if (_disposed)
            return;
        int version = ++_scanVersion;
        string? path = _player.Playlist.Url;
        if (_player.Playlist.Items.Count != 1 || !FileHelper.IsLocalMedia(path))
        {
            _directoryItems = null;
            DirectoryError = null;
            NotifyPlaylist();
            return;
        }
        List<string> files;
        string? error = null;
        try { files = await Task.Run(() => FileHelper.GetDirectoryMedia(path!)); }
        catch (Exception ex)
        {
            _log.Warn($"Cannot read media directory: {ex.Message}");
            error = ex.Message;
            files = [path!];
        }
        if (_disposed || version != _scanVersion || !SamePath(path, _player.Playlist.Url))
            return;
        _directoryPath = path;
        _directoryItems = files.Select(file => new DirectoryPlaylistItem(file, SamePath(file, path))).ToList();
        DirectoryError = error;
        NotifyPlaylist();
    }

    public void Save()
    {
        if (_disposed || _player.IsDisposed || _player.IsOpening || !_player.CanPlay
            || _activePath == null || !SamePath(_activePath, _player.Playlist.Url)
            || !SamePath(_activePath, _latestUrl))
            return;
        var state = new PlaybackState(_activePath,
            _player.Status == Status.Ended ? 0 : Math.Max(0, _player.CurTime), _player.Duration);
        _lastState = state;
        OnPropertyChanged(nameof(HasHistory));
        try { _store.Save(state); }
        catch (Exception ex) { _log.Warn($"Cannot save playback history: {ex.Message}"); }
    }

    private void OnPlaylistChanged(object? sender, PropertyChangedEventArgs args) => Post(NotifyPlaylist);
    private void OnPlaylistItemsChanged(object? sender, NotifyCollectionChangedEventArgs args) => Post(NotifyPlaylist);
    private void NotifyPlaylist()
    {
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(ExpectedCount));
        OnPropertyChanged(nameof(PreviousItem));
        OnPropertyChanged(nameof(NextItem));
        OnPropertyChanged(nameof(DirectoryError));
    }
    private void Post(Action action) => _dispatcher.BeginInvoke(() => { if (!_disposed) action(); });
    private static bool SamePath(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_disposed) return;
        Save();
        _disposed = true;
        ++_scanVersion;
        _saveTimer.Stop();
        _player.PlayRequestedWithoutMedia -= OnPlayWithoutMedia;
        _player.Opening -= OnOpening;
        _player.OpenCompleted -= OnOpenCompleted;
        _player.OpenSessionCompleted -= OnSessionCompleted;
        _player.OpenPlaylistItemCompleted -= OnPlaylistItemCompleted;
        _player.Playlist.PropertyChanged -= OnPlaylistChanged;
        _player.Playlist.Items.CollectionChanged -= OnPlaylistItemsChanged;
    }
}

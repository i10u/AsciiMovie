using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AsciiMovie.Core;

namespace AsciiMovie.Player;

public sealed class PlaybackController : IDisposable
{
    private readonly MediaElement _mediaElement;
    private readonly DispatcherTimer _timer;
    private AmReader? _reader;
    private FileStream? _fileStream;
    private AmHeader? _videoHeader;
    private string? _videoPath;
    private string? _tempAudioPath;
    private bool _isPlaying;
    private bool _isDragging;
    private int _currentFrame;
    private Stopwatch? _stopwatch;
    private double _clockOffset;
    private double _volume = 0.8;

    public PlaybackController(MediaElement mediaElement)
    {
        _mediaElement = mediaElement;
        _mediaElement.LoadedBehavior = MediaState.Manual;
        _mediaElement.UnloadedBehavior = MediaState.Manual;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0),
        };
        _timer.Tick += (_, _) => UpdateFrameFromClock();
    }

    public AmHeader? Header => _reader?.Header ?? _videoHeader;

    public bool IsVideoSource => _videoHeader != null;

    public string? VideoSourcePath => _videoPath;

    public int CurrentFrame => _currentFrame;

    public int FrameCount => (int?)Header?.FrameCount ?? 0;

    public bool IsPlaying => _isPlaying;
    public double Volume => _volume;

    public double DurationSeconds =>
        Header == null ? 0 : Header.FrameCount / Math.Max(Header.Fps, 0.001f);

    public double CurrentTimeSeconds => _clockOffset + (_stopwatch?.Elapsed.TotalSeconds ?? 0);

    public event Action<int>? FrameChanged;
    public event Action? StateChanged;

    public void Load(string path)
    {
        ClearVideoSource();
        HaltPlayback(clearMediaSource: true);
        DisposeReader();

        _fileStream = File.OpenRead(path);
        _reader = new AmReader(_fileStream);

        if (_reader.Header.HasAudio && _reader.Header.Audio.Length > 0)
        {
            var ext = _reader.Header.AudioCodec == AmAudioCodec.Aac ? ".aac" : ".mp3";
            _tempAudioPath = Path.Combine(Path.GetTempPath(), $"asciimovie_{Guid.NewGuid():N}{ext}");
            File.WriteAllBytes(_tempAudioPath, _reader.Header.Audio);
            _mediaElement.Source = new Uri(_tempAudioPath);
            _mediaElement.Volume = _volume;
        }
        else
        {
            _mediaElement.Source = null;
        }

        _currentFrame = 0;
        _clockOffset = 0;
        FrameChanged?.Invoke(_currentFrame);
        StateChanged?.Invoke();
    }

    public void UpdateVideoGrid(int cols, int rows)
    {
        if (_videoHeader == null)
            return;

        _videoHeader.Cols = (ushort)cols;
        _videoHeader.Rows = (ushort)rows;
    }

    public void LoadVideo(string path, AmHeader syntheticHeader)
    {
        DisposeReader();
        HaltPlayback(clearMediaSource: false);

        _videoPath = path;
        _videoHeader = syntheticHeader;
        _mediaElement.Source = new Uri(path);
        _mediaElement.Volume = _volume;

        _currentFrame = 0;
        _clockOffset = 0;
        FrameChanged?.Invoke(_currentFrame);
        StateChanged?.Invoke();
    }

    public void Play()
    {
        if (Header == null || _isPlaying)
            return;

        if (_currentFrame >= FrameCount - 1)
            SeekFrame(0);

        _isPlaying = true;
        _stopwatch = Stopwatch.StartNew();
        StartAudio();
        _timer.Start();
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        if (!_isPlaying)
            return;

        _isPlaying = false;
        _timer.Stop();
        AccumulateClock();
        PauseAudio();
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        if (Header == null)
            return;

        _isPlaying = false;
        _timer.Stop();
        _stopwatch = null;
        _clockOffset = 0;
        _currentFrame = 0;
        StopAudio();

        FrameChanged?.Invoke(_currentFrame);
        StateChanged?.Invoke();
    }

    public void SeekFrame(int frame)
    {
        if (Header == null)
            return;

        frame = Math.Clamp(frame, 0, FrameCount - 1);
        _currentFrame = frame;
        _clockOffset = frame / Math.Max(Header.Fps, 0.001f);

        if (_isPlaying)
            _stopwatch = Stopwatch.StartNew();
        else
            _stopwatch = null;

        SyncAudioToClock();
        FrameChanged?.Invoke(_currentFrame);
    }

    public void SetDragging(bool dragging) => _isDragging = dragging;

    public void SetVolume(double value)
    {
        _volume = Math.Clamp(value, 0.0, 1.0);
        _mediaElement.Volume = _volume;
    }

    public byte[] ReadFrame(int index)
    {
        if (_reader == null)
            throw new InvalidOperationException("No .amov file loaded.");
        return _reader.ReadFrame(index);
    }

    private void StartAudio()
    {
        if (_mediaElement.Source == null)
            return;

        SyncAudioToClock();
        _mediaElement.Play();
    }

    private void PauseAudio()
    {
        if (_mediaElement.Source == null)
            return;

        _mediaElement.Pause();
    }

    private void StopAudio()
    {
        if (_mediaElement.Source == null)
            return;

        _mediaElement.Stop();
        _mediaElement.Position = TimeSpan.Zero;
    }

    private void SyncAudioToClock()
    {
        if (_mediaElement.Source == null)
            return;

        var target = TimeSpan.FromSeconds(CurrentTimeSeconds);
        if (_mediaElement.NaturalDuration.HasTimeSpan)
        {
            var duration = _mediaElement.NaturalDuration.TimeSpan;
            if (target > duration)
                target = duration;
        }

        if (target < TimeSpan.Zero)
            target = TimeSpan.Zero;

        _mediaElement.Position = target;
    }

    private void AccumulateClock()
    {
        if (_stopwatch == null)
            return;

        _clockOffset += _stopwatch.Elapsed.TotalSeconds;
        _stopwatch = null;
    }

    private void HaltPlayback(bool clearMediaSource)
    {
        _isPlaying = false;
        _timer.Stop();
        _stopwatch = null;
        _clockOffset = 0;
        _currentFrame = 0;

        if (_mediaElement.Source != null)
        {
            _mediaElement.Stop();
            if (clearMediaSource)
                _mediaElement.Source = null;
            else
                _mediaElement.Position = TimeSpan.Zero;
        }
    }

    private void ClearVideoSource()
    {
        _videoPath = null;
        _videoHeader = null;
    }

    private void UpdateFrameFromClock()
    {
        var header = Header;
        if (header == null || _isDragging || !_isPlaying)
            return;

        var seconds = CurrentTimeSeconds;
        var duration = DurationSeconds;
        if (seconds >= duration)
        {
            _clockOffset = duration;
            _currentFrame = Math.Max(0, FrameCount - 1);
            FrameChanged?.Invoke(_currentFrame);
            Pause();
            return;
        }

        var frame = (int)Math.Round(seconds * header.Fps);
        frame = Math.Clamp(frame, 0, FrameCount - 1);

        if (frame != _currentFrame)
        {
            _currentFrame = frame;
            FrameChanged?.Invoke(_currentFrame);
        }
    }

    private void DisposeReader()
    {
        _reader?.Dispose();
        _reader = null;
        _fileStream?.Dispose();
        _fileStream = null;

        if (_tempAudioPath != null && File.Exists(_tempAudioPath))
        {
            try { File.Delete(_tempAudioPath); }
            catch { /* best effort */ }
            _tempAudioPath = null;
        }
    }

    public void Dispose()
    {
        HaltPlayback(clearMediaSource: true);
        ClearVideoSource();
        DisposeReader();
        _timer.Stop();
    }
}

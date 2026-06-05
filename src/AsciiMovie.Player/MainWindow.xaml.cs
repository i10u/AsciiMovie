using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AsciiMovie.Core;
using Microsoft.Win32;

namespace AsciiMovie.Player;

public partial class MainWindow : Window
{
    private const string FfmpegPath = "ffmpeg";
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    private readonly PlaybackController _playback;
    private readonly AsciiFrameRectSelection _rectSelection;
    private readonly FrameRenderSettings _previewSettings = new();
    private readonly DispatcherTimer _previewDebounce;

    private bool _updatingSlider;
    private bool _suppressSettingsEvents;
    private int _displayFrameIndex = -1;
    private string _displaySettingsKey = "";
    private DateTime _lastVideoRenderUtc = DateTime.MinValue;
    private const int VideoRenderIntervalMs = 450;
    private string[,]? _cellGrid;
    private int _pendingRenderFrame = -1;
    private bool _renderPosted;
    private CancellationTokenSource? _displayCts;
    private bool _videoRefreshInFlight;
    private AmHeader? _displayHeader;
    private byte[]? _displayedFrameData;
    private bool _isReady;
    private bool _updatingVolumeUi;
    private bool _isMuted;
    private double _lastNonZeroVolume = 0.8;

    public MainWindow()
    {
        _suppressSettingsEvents = true;
        InitializeComponent();

        _previewDebounce = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };

        var copyBinding = new CommandBinding(ApplicationCommands.Copy, Copy_Executed, Copy_CanExecute);
        FrameEditor.CommandBindings.Add(copyBinding);
        FrameEditor.InputBindings.Add(new KeyBinding(ApplicationCommands.Copy, Key.C, ModifierKeys.Control));
        CommandManager.AddPreviewExecutedHandler(FrameEditor, OnPreviewCopy);

        _playback = new PlaybackController(AudioPlayer);
        _rectSelection = new AsciiFrameRectSelection(
            FrameEditor,
            () => !_playback.IsPlaying,
            () => _displayHeader ?? _playback.Header,
            () => _cellGrid);
        _playback.FrameChanged += OnFrameChanged;
        _playback.StateChanged += UpdateUiState;
        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            InvalidateDisplayCache();
            ScheduleDisplayRefresh();
        };
        Closed += (_, _) =>
        {
            _displayCts?.Cancel();
            _playback.Dispose();
        };

        _suppressSettingsEvents = false;
        _isReady = true;
        ReadPreviewSettingsFromUi();
        _playback.SetVolume(VolumeSlider.Value);
        UpdateVolumeUi();
        UpdateSettingsLabels();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "AsciiMovie (*.amov)|*.amov|動画/画像 (*.mp4;*.mkv;*.avi;*.mov;*.webm;*.wmv;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.wmv;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|すべて (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var path = dialog.FileName;
            if (path.EndsWith(".amov", StringComparison.OrdinalIgnoreCase))
                OpenAmov(path);
            else if (IsImagePath(path))
                OpenImage(path);
            else
                await OpenVideoAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ファイルを開けません", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenAmov(string path)
    {
        _playback.Load(path);
        Title = $"AsciiMovie Player — {System.IO.Path.GetFileName(path)}";
        SeekSlider.Maximum = Math.Max(0, _playback.FrameCount - 1);
        SeekSlider.Value = 0;
        InvalidateDisplayCache();
        _rectSelection.ClearSelection();
        _cellGrid = null;
        ApplyHeaderToPreviewSettings(_playback.Header!);
        UpdateSettingsLabels();
        UpdateTimeLabel();
        ScheduleDisplayRefresh();
        UpdateUiState();
    }

    private async Task OpenVideoAsync(string path)
    {
        VideoProbe.EnsureFfmpegAvailable(FfmpegPath);
        var probe = await VideoProbe.ProbeAsync(FfmpegPath, path).ConfigureAwait(true);
        FitGridToVideoAspect(probe.Width, probe.Height);
        ReadPreviewSettingsFromUi();
        var fps = probe.Fps > 0 ? probe.Fps : 24f;
        var frameCount = Math.Max(1, (int)Math.Round(probe.DurationSeconds * fps));

        var header = new AmHeader
        {
            Version = AmHeader.CurrentVersion,
            Cols = (ushort)_previewSettings.Cols,
            Rows = (ushort)_previewSettings.Rows,
            Fps = fps,
            FrameCount = (uint)frameCount,
            Charset = AsciiMapper.DefaultCharset,
            Flags = _previewSettings.Color ? AmFlags.Color : AmFlags.None,
        };

        _playback.LoadVideo(path, header);
        Title = $"AsciiMovie Player — {System.IO.Path.GetFileName(path)} (動的)";
        SeekSlider.Maximum = Math.Max(0, _playback.FrameCount - 1);
        SeekSlider.Value = 0;
        InvalidateDisplayCache();
        _rectSelection.ClearSelection();
        _cellGrid = null;
        ApplyHeaderToPreviewSettings(header);
        UpdateSettingsLabels();
        UpdateTimeLabel();
        ScheduleDisplayRefresh();
        UpdateUiState();
    }

    private void OpenImage(string path)
    {
        VideoProbe.EnsureFfmpegAvailable(FfmpegPath);
        var (width, height) = ReadImageSize(path);
        FitGridToVideoAspect(width, height);
        ReadPreviewSettingsFromUi();

        var header = new AmHeader
        {
            Version = AmHeader.CurrentVersion,
            Cols = (ushort)_previewSettings.Cols,
            Rows = (ushort)_previewSettings.Rows,
            Fps = 1,
            FrameCount = 1,
            Charset = AsciiMapper.DefaultCharset,
            Flags = _previewSettings.Color ? AmFlags.Color : AmFlags.None,
        };

        _playback.LoadVideo(path, header);
        Title = $"AsciiMovie Player — {System.IO.Path.GetFileName(path)} (画像)";
        SeekSlider.Maximum = 0;
        SeekSlider.Value = 0;
        InvalidateDisplayCache();
        _rectSelection.ClearSelection();
        _cellGrid = null;
        ApplyHeaderToPreviewSettings(header);
        UpdateSettingsLabels();
        UpdateTimeLabel();
        ScheduleDisplayRefresh();
        UpdateUiState();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_playback.Header == null)
            return;

        if (_playback.IsPlaying)
            _playback.Pause();
        else
        {
            _rectSelection.ClearSelection();
            _playback.Play();
        }

        UpdatePlayPauseButton();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        CancelPendingRender();
        _displayCts?.Cancel();
        _playback.Stop();
        _updatingSlider = true;
        SeekSlider.Value = 0;
        _updatingSlider = false;
        InvalidateDisplayCache();
        _rectSelection.ClearSelection();
        _cellGrid = null;
        UpdatePlayPauseButton();
        ScheduleDisplayRefresh();
        UpdateTimeLabel();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void PreviewSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isReady || _suppressSettingsEvents)
            return;

        ReadPreviewSettingsFromUi();
        if (_playback.IsVideoSource)
            _playback.UpdateVideoGrid(_previewSettings.Cols, _previewSettings.Rows);
        UpdateSettingsLabels();
        InvalidateDisplayCache();

        var heavySlider = sender is Slider s
            && (ReferenceEquals(s, ColsSlider) || ReferenceEquals(s, RowsSlider) || ReferenceEquals(s, EdgeStrengthSlider));

        if (heavySlider && CanRegenerateFromVideo())
        {
            _previewDebounce.Stop();
            _previewDebounce.Start();
            return;
        }

        RequestDisplayRefresh(urgent: true);
    }

    private void InvalidateDisplayCache()
    {
        _displayFrameIndex = -1;
        _displaySettingsKey = "";
    }

    private string CurrentDisplaySettingsKey() =>
        $"{_previewSettings.Cols}:{_previewSettings.Rows}:{_previewSettings.UseEdge}:{_previewSettings.EdgeStrength:F2}:{_previewSettings.Color}:{FontSizeSlider.Value:F0}:{SelectedFontFamilyName()}";

    private bool CanRegenerateFromVideo() => _playback.IsVideoSource;

    private void RequestDisplayRefresh(bool urgent = false)
    {
        if (_playback.IsVideoSource && _videoRefreshInFlight && !urgent)
            return;

        if (_playback.IsVideoSource && _playback.IsPlaying && !urgent)
        {
            var elapsed = (DateTime.UtcNow - _lastVideoRenderUtc).TotalMilliseconds;
            if (elapsed < VideoRenderIntervalMs)
                return;
        }

        if (_playback.IsVideoSource)
        {
            _lastVideoRenderUtc = DateTime.UtcNow;
            _videoRefreshInFlight = true;
        }

        ScheduleDisplayRefresh();
    }

    private void OnPreviewCopy(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Copy || !ReferenceEquals(sender, FrameEditor))
            return;

        if (TryCopyFrameContent())
            e.Handled = true;
    }

    private void Copy_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !_playback.IsPlaying && _playback.Header != null;
    }

    private void Copy_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (TryCopyFrameContent())
            e.Handled = true;
    }

    private bool TryCopyFrameContent()
    {
        if (_playback.IsPlaying || _playback.Header == null)
            return false;

        if (_rectSelection.TryCopyToClipboard())
            return true;

        var header = _displayHeader ?? _playback.Header;
        var frameData = _displayedFrameData ?? _playback.ReadFrame(_playback.CurrentFrame);
        Clipboard.SetText(AsciiFrameText.BuildPlainText(header!, frameData));
        return true;
    }

    private void OnFrameChanged(int frame)
    {
        _updatingSlider = true;
        SeekSlider.Value = frame;
        _updatingSlider = false;
        UpdateTimeLabel();

        if (_playback.IsVideoSource)
        {
            if (!_playback.IsPlaying)
            {
                RequestDisplayRefresh(urgent: true);
            }
            else
            {
                RequestDisplayRefresh();
            }
        }
        else if (!_playback.IsPlaying)
            ScheduleDisplayRefresh();
        else
            SchedulePlaybackRender(frame);
    }

    private void SchedulePlaybackRender(int frame)
    {
        _pendingRenderFrame = frame;
        if (_renderPosted)
            return;

        _renderPosted = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, RenderPendingFrame);
    }

    private void RenderPendingFrame()
    {
        _renderPosted = false;
        if (!_playback.IsPlaying)
            return;

        var frame = _pendingRenderFrame;
        if (frame != _playback.CurrentFrame)
        {
            _pendingRenderFrame = _playback.CurrentFrame;
            _renderPosted = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, RenderPendingFrame);
            return;
        }

        RenderStoredFrame();
    }

    private void CancelPendingRender()
    {
        _pendingRenderFrame = -1;
        _renderPosted = false;
    }

    private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        _playback.SetDragging(true);

    private void SeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _playback.SetDragging(false);
        _playback.SeekFrame((int)SeekSlider.Value);
        InvalidateDisplayCache();
        ScheduleDisplayRefresh();
        UpdateTimeLabel();
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSlider || _playback.Header == null)
            return;

        if (SeekSlider.IsMouseCaptured)
        {
            _playback.SeekFrame((int)e.NewValue);
            InvalidateDisplayCache();
            ScheduleDisplayRefresh();
            UpdateTimeLabel();
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isReady || _updatingVolumeUi)
            return;

        var volume = Math.Clamp(e.NewValue, 0.0, 1.0);
        _playback.SetVolume(volume);
        if (volume > 0)
        {
            _lastNonZeroVolume = volume;
            _isMuted = false;
        }
        else
        {
            _isMuted = true;
        }

        UpdateVolumeUi();
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isMuted)
        {
            var restore = _lastNonZeroVolume > 0 ? _lastNonZeroVolume : 0.8;
            SetVolumeUiAndPlayback(restore);
            _isMuted = false;
        }
        else
        {
            if (VolumeSlider.Value > 0)
                _lastNonZeroVolume = VolumeSlider.Value;
            SetVolumeUiAndPlayback(0);
            _isMuted = true;
        }

        UpdateVolumeUi();
    }

    private void ScheduleDisplayRefresh()
    {
        _displayCts?.Cancel();
        _displayCts = new CancellationTokenSource();
        var token = _displayCts.Token;
        _ = RefreshDisplayAsync(token);
    }

    private async Task RefreshDisplayAsync(CancellationToken token)
    {
        var fileHeader = _playback.Header;
        if (fileHeader == null)
        {
            _videoRefreshInFlight = false;
            return;
        }

        try
        {
            if (_playback.IsVideoSource)
            {
                var rgb = await VideoFrameExtractor.ExtractFrameAsync(
                    FfmpegPath,
                    _playback.VideoSourcePath!,
                    _playback.CurrentTimeSeconds,
                    _previewSettings.Cols,
                    _previewSettings.Rows,
                    token).ConfigureAwait(false);

                if (token.IsCancellationRequested || rgb == null)
                    return;

                var mapped = FrameRenderer.MapRgbFrame(rgb, _previewSettings);
                var displayHeader = BuildPreviewHeader(fileHeader);
                await Dispatcher.InvokeAsync(() => ApplyFrameToView(displayHeader, mapped));
                return;
            }

            if (token.IsCancellationRequested)
                return;

            await Dispatcher.InvokeAsync(RenderStoredFrame);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show(this, ex.Message, "プレビュー", MessageBoxButton.OK, MessageBoxImage.Warning));
        }
        finally
        {
            _videoRefreshInFlight = false;
        }
    }

    private void RenderStoredFrame()
    {
        var fileHeader = _playback.Header;
        if (fileHeader == null)
        {
            FrameEditor.Document = new();
            SelectionHint.Visibility = Visibility.Collapsed;
            return;
        }

        if (_playback.IsVideoSource)
            return;

        SelectionHint.Visibility = _playback.IsPlaying ? Visibility.Collapsed : Visibility.Visible;

        var settingsKey = CurrentDisplaySettingsKey();
        if (_playback.CurrentFrame == _displayFrameIndex
            && settingsKey == _displaySettingsKey
            && _displayedFrameData != null)
            return;

        var displayHeader = BuildPreviewHeader(fileHeader);
        var frameData = _playback.ReadFrame(_playback.CurrentFrame);
        if (displayHeader.Cols != fileHeader.Cols || displayHeader.Rows != fileHeader.Rows)
            frameData = AmFrameResampler.Resample(frameData, fileHeader, displayHeader.Cols, displayHeader.Rows);

        ApplyFrameToView(displayHeader, frameData);
        _displayFrameIndex = _playback.CurrentFrame;
        _displaySettingsKey = settingsKey;
    }

    private void ApplyFrameToView(AmHeader header, byte[] frameData)
    {
        var forceMono = ColorCheck.IsChecked != true;
        var fontSize = FontSizeSlider.Value;
        var fontFamily = new FontFamily(SelectedFontFamilyName());
        FrameEditor.FontSize = fontSize;
        FrameEditor.FontFamily = fontFamily;
        FrameEditor.Document = AsciiFrameText.BuildDocument(header, frameData, forceMono, fontSize, fontFamily);
        _displayHeader = header;
        _displayedFrameData = frameData;
        _cellGrid = AsciiFrameRectSelection.BuildCellGrid(header, frameData);
        _displayFrameIndex = _playback.CurrentFrame;
        _displaySettingsKey = CurrentDisplaySettingsKey();
        _rectSelection.ClearSelection();
    }

    private AmHeader BuildPreviewHeader(AmHeader fileHeader) => new()
    {
        Version = fileHeader.Version,
        Cols = (ushort)_previewSettings.Cols,
        Rows = (ushort)_previewSettings.Rows,
        Fps = fileHeader.Fps,
        FrameCount = fileHeader.FrameCount,
        Charset = _previewSettings.Charset,
        Flags = _previewSettings.Color ? AmFlags.Color : AmFlags.None,
    };

    private void ApplyHeaderToPreviewSettings(AmHeader header)
    {
        _suppressSettingsEvents = true;
        _previewSettings.Cols = header.Cols;
        _previewSettings.Rows = header.Rows;
        _previewSettings.Charset = header.Charset;
        _previewSettings.Color = header.HasColor;
        ColsSlider.Value = header.Cols;
        RowsSlider.Value = header.Rows;
        ColorCheck.IsChecked = header.HasColor;
        _suppressSettingsEvents = false;
        ReadPreviewSettingsFromUi();
    }

    private void ReadPreviewSettingsFromUi()
    {
        _previewSettings.Cols = (int)ColsSlider.Value;
        _previewSettings.Rows = (int)RowsSlider.Value;
        _previewSettings.UseEdge = EdgeCheck.IsChecked == true;
        _previewSettings.EdgeStrength = EdgeStrengthSlider.Value / 100.0;
        _previewSettings.Color = ColorCheck.IsChecked == true;
        _previewSettings.Charset = _playback.Header?.Charset ?? AsciiMapper.DefaultCharset;
    }

    private void UpdateSettingsLabels()
    {
        ColsLabel.Text = $"{_previewSettings.Cols}";
        RowsLabel.Text = $"{_previewSettings.Rows}";
        EdgeStrengthLabel.Text = $"{EdgeStrengthSlider.Value:0}%";
        FontSizeLabel.Text = $"{FontSizeSlider.Value:0}";
    }

    private void SetVolumeUiAndPlayback(double value)
    {
        _updatingVolumeUi = true;
        VolumeSlider.Value = Math.Clamp(value, 0.0, 1.0);
        _updatingVolumeUi = false;
        _playback.SetVolume(VolumeSlider.Value);
    }

    private void UpdateVolumeUi()
    {
        VolumeLabel.Text = $"{VolumeSlider.Value * 100:0}%";
        MuteButton.Content = _isMuted || VolumeSlider.Value <= 0 ? "ミュート解除" : "ミュート";
    }

    private string SelectedFontFamilyName() =>
        (FontFamilyCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Consolas";

    private void FitGridToVideoAspect(int videoWidth, int videoHeight)
    {
        if (videoWidth <= 0 || videoHeight <= 0)
            return;

        var fontSize = FontSizeSlider.Value;
        var fontFamily = new FontFamily(SelectedFontFamilyName());
        var (cellW, cellH) = MeasureCell(fontFamily, fontSize);
        var viewportW = FrameEditor.ViewportWidth > 0 ? FrameEditor.ViewportWidth : Math.Max(120, FrameEditor.ActualWidth - 20);
        var viewportH = FrameEditor.ViewportHeight > 0 ? FrameEditor.ViewportHeight : Math.Max(80, FrameEditor.ActualHeight - 20);
        var maxColsByViewport = Math.Max(1, (int)Math.Floor(viewportW / Math.Max(1, cellW)));
        var maxRowsByViewport = Math.Max(1, (int)Math.Floor(viewportH / Math.Max(1, cellH)));

        var videoAspect = (double)videoWidth / videoHeight;
        var cellAspect = cellW / Math.Max(1, cellH);
        var sliderMaxCols = (int)ColsSlider.Maximum;
        var sliderMaxRows = (int)RowsSlider.Maximum;
        var sliderMinCols = (int)ColsSlider.Minimum;
        var sliderMinRows = (int)RowsSlider.Minimum;

        var maxCols = Math.Min(sliderMaxCols, maxColsByViewport);
        var maxRows = Math.Min(sliderMaxRows, maxRowsByViewport);
        var targetRows = Math.Max(sliderMinRows, Math.Min(maxRows, (int)Math.Round(RowsSlider.Value)));
        var targetCols = (int)Math.Round(targetRows * videoAspect / Math.Max(0.0001, cellAspect));

        if (targetCols > maxCols)
        {
            targetCols = maxCols;
            targetRows = (int)Math.Round(targetCols * cellAspect / Math.Max(0.0001, videoAspect));
        }

        if (targetRows > maxRows)
        {
            targetRows = maxRows;
            targetCols = (int)Math.Round(targetRows * videoAspect / Math.Max(0.0001, cellAspect));
        }

        targetCols = Math.Clamp(targetCols, sliderMinCols, sliderMaxCols);
        targetRows = Math.Clamp(targetRows, sliderMinRows, sliderMaxRows);

        _suppressSettingsEvents = true;
        ColsSlider.Value = targetCols;
        RowsSlider.Value = targetRows;
        _suppressSettingsEvents = false;
    }

    private static (double Width, double Height) MeasureCell(FontFamily family, double fontSize)
    {
        var text = new FormattedText(
            "M",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            fontSize,
            Brushes.White,
            1.0);
        return (Math.Max(1, text.WidthIncludingTrailingWhitespace), Math.Max(1, text.Height));
    }

    private void UpdateTimeLabel()
    {
        var current = FormatTime(_playback.CurrentTimeSeconds);
        var total = FormatTime(_playback.DurationSeconds);
        TimeLabel.Text = $"{current} / {total}  (frame {_playback.CurrentFrame + 1}/{Math.Max(1, _playback.FrameCount)})";
    }

    private void UpdateUiState()
    {
        UpdatePlayPauseButton();
        if (!_playback.IsPlaying)
        {
            CancelPendingRender();
            InvalidateDisplayCache();
            ScheduleDisplayRefresh();
            if (_playback.Header != null)
                FrameEditor.Focus();
        }
    }

    private void UpdatePlayPauseButton()
    {
        PlayPauseButton.Content = _playback.IsPlaying ? "一時停止" : "再生";
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }

    private static bool IsImagePath(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return ImageExtensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    private static (int Width, int Height) ReadImageSize(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault() ?? throw new InvalidOperationException("画像を読み込めませんでした。");
        return (Math.Max(1, frame.PixelWidth), Math.Max(1, frame.PixelHeight));
    }
}

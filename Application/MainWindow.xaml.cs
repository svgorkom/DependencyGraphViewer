using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using System.Windows.Threading;
using GraphParser;
using GraphParser.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace DependencyGraphViewer;

public partial class MainWindow : Window
{
    private const double MinPlaybackIntervalMs = 80;
    private const double BasePlaybackIntervalMs = 400;

    private IReadOnlyList<GraphAction>? _actions;
    private readonly DispatcherTimer _playbackTimer = new();
    private readonly UserSettings _settings = UserSettings.Load();
    private bool _isPlaying;
    private double _speedMultiplier = 1.0;
    private int _highlightDepth = 1;
    private double _timeScaleFactor = 1.0;
    private bool _webViewReady;
    private GraphSnapshot? _currentSnapshot;
    private bool _suppressJobListSelection;
    private bool _suppressExitListSelection;

    public MainWindow()
    {
        InitializeComponent();

        _playbackTimer.Tick += OnPlaybackTick;
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(BasePlaybackIntervalMs);

        Loaded += OnWindowLoaded;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await GraphWebView.EnsureCoreWebView2Async();

            var webContentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebContent");
            GraphWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.local", webContentPath, CoreWebView2HostResourceAccessKind.Allow);

            GraphWebView.NavigationCompleted += async (_, args) =>
            {
                try
                {
                    _webViewReady = args.IsSuccess;

                    if (_webViewReady
                        && _actions is null
                        && !string.IsNullOrEmpty(_settings.LastFilePath)
                        && File.Exists(_settings.LastFilePath))
                    {
                        await LoadFileAsync(_settings.LastFilePath);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            GraphWebView.CoreWebView2.Navigate("https://app.local/graph.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnOpenFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            Title = "Open Sequencing Graph Actions CSV"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await LoadFileAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load file: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestore(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // Toggle the maximize/restore glyph.
        MaxRestoreButton.Content = WindowState == WindowState.Maximized
            ? "\uE923"   // Restore
            : "\uE922";  // Maximize
    }

    private async Task LoadFileAsync(string filePath)
    {
        StopPlayback();
        SetLoadingState(true);

        var actions = await Task.Run(() => SequencingGraphCsvParser.Parse(filePath));

        _actions = actions;
        FileNameLabel.Text = Path.GetFileName(filePath);
        _timeScaleFactor = ComputeTimeScaleFactor(_actions);

        _settings.LastFilePath = filePath;
        _settings.Save();

        if (_actions.Count > 0)
        {
            TimeSlider.Maximum = _actions.Count - 1;
            TimeSlider.Value = 0;
            TimeSlider.IsEnabled = true;
            PlayPauseButton.IsEnabled = true;
            ResetViewButton.IsEnabled = true;
            ResetLayoutButton.IsEnabled = true;
            PlaceholderText.Visibility = Visibility.Collapsed;
            GraphWebView.Visibility = Visibility.Visible;

            UpdateGraph();
        }
        else
        {
            TimeSlider.Maximum = 0;
            TimeSlider.IsEnabled = false;
            PlayPauseButton.IsEnabled = false;
            ResetViewButton.IsEnabled = false;
            ResetLayoutButton.IsEnabled = false;
            TimestampLabel.Text = "—";
            ActionCountLabel.Text = string.Empty;
            await SendGraphToViewAsync(new GraphSnapshot());
            PlaceholderText.Visibility = Visibility.Visible;
            GraphWebView.Visibility = Visibility.Collapsed;
        }

        SetLoadingState(false);
    }

    private void SetLoadingState(bool isLoading)
    {
        IsEnabled = !isLoading;
        if (isLoading)
            FileNameLabel.Text = "Loading…";
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateGraph();
    }

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
            StopPlayback();
        else
            StartPlayback();
    }

    private void StartPlayback()
    {
        if (_actions is null || _actions.Count == 0)
            return;

        if ((int)TimeSlider.Value >= _actions.Count - 1)
            TimeSlider.Value = 0;

        _isPlaying = true;
        PlayPauseButton.Content = "⏸";
        ScheduleNextTick();
    }

    private void StopPlayback()
    {
        _isPlaying = false;
        _playbackTimer.Stop();
        PlayPauseButton.Content = "▶";
    }

    private void ScheduleNextTick()
    {
        if (_actions is null)
            return;

        var current = (int)TimeSlider.Value;
        if (current >= _actions.Count - 1)
        {
            StopPlayback();
            return;
        }

        var delta = _actions[current + 1].Timestamp - _actions[current].Timestamp;
        double intervalMs = delta.TotalMilliseconds > 0
            ? delta.TotalMilliseconds * _timeScaleFactor / _speedMultiplier
            : BasePlaybackIntervalMs / _speedMultiplier;

        intervalMs = Math.Clamp(intervalMs, MinPlaybackIntervalMs, 5000);

        _playbackTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        _playbackTimer.Start();
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        _playbackTimer.Stop();

        if (!_isPlaying || _actions is null)
            return;

        var next = (int)TimeSlider.Value + 1;
        if (next >= _actions.Count)
        {
            StopPlayback();
            return;
        }

        TimeSlider.Value = next;
        ScheduleNextTick();
    }

    private void OnSpeedDecrease(object sender, RoutedEventArgs e)
    {
        if (_speedMultiplier > 1)
        {
            _speedMultiplier--;
            SpeedLabel.Text = $"{(int)_speedMultiplier}×";
        }
    }

    private void OnSpeedIncrease(object sender, RoutedEventArgs e)
    {
        if (_speedMultiplier < 20)
        {
            _speedMultiplier++;
            SpeedLabel.Text = $"{(int)_speedMultiplier}×";
        }
    }

    private async void OnHighlightDepthDecrease(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_highlightDepth > 1)
            {
                _highlightDepth--;
                HighlightDepthLabel.Text = _highlightDepth.ToString();
                if (_webViewReady)
                    await GraphWebView.ExecuteScriptAsync($"setHighlightDepth({_highlightDepth})");
            }
        }
        catch (Exception)
        {
            // Non-critical — avoid crash on transient WebView errors.
        }
    }

    private async void OnHighlightDepthIncrease(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_highlightDepth < 10)
            {
                _highlightDepth++;
                HighlightDepthLabel.Text = _highlightDepth.ToString();
                if (_webViewReady)
                    await GraphWebView.ExecuteScriptAsync($"setHighlightDepth({_highlightDepth})");
            }
        }
        catch (Exception)
        {
            // Non-critical — avoid crash on transient WebView errors.
        }
    }

    private static double ComputeTimeScaleFactor(IReadOnlyList<GraphAction> actions)
    {
        if (actions.Count < 2)
            return 1.0;

        var deltas = new List<double>();
        for (var i = 1; i < actions.Count; i++)
        {
            var ms = (actions[i].Timestamp - actions[i - 1].Timestamp).TotalMilliseconds;
            if (ms > 0)
                deltas.Add(ms);
        }

        if (deltas.Count == 0)
            return 1.0;

        deltas.Sort();
        var median = deltas[deltas.Count / 2];

        return median >= BasePlaybackIntervalMs
            ? 1.0
            : BasePlaybackIntervalMs / median;
    }

    private async void UpdateGraph()
    {
        try
        {
            if (_actions is null || _actions.Count == 0)
                return;

            var index = (int)TimeSlider.Value;
            if (index < 0 || index >= _actions.Count)
                return;

            var actions = _actions;
            var snapshot = await Task.Run(() => SequencingGraphCsvParser.BuildSnapshot(actions, index));

            TimestampLabel.Text = actions[index].Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
            ActionCountLabel.Text = $"Action {index + 1} of {actions.Count}  ·  {actions[index].ActionType}";

            _currentSnapshot = snapshot;
            PopulateJobList();
            PopulateExitList();

            await SendGraphToViewAsync(snapshot);
        }
        catch (Exception)
        {
            // Non-critical — avoid crash on transient errors during graph update.
        }
    }

    private async void OnResetView(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_webViewReady)
                return;

            await GraphWebView.ExecuteScriptAsync("resetView()");
        }
        catch (Exception)
        {
            // Non-critical — avoid crash on transient WebView errors.
        }
    }

    private async void OnResetLayout(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_webViewReady)
                return;

            await GraphWebView.ExecuteScriptAsync("resetLayout()");
        }
        catch (Exception)
        {
            // Non-critical — avoid crash on transient WebView errors.
        }
    }

    private void PopulateJobList()
    {
        _suppressJobListSelection = true;
        try
        {
            var allJobIds = _currentSnapshot?.Nodes.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList()
                            ?? [];

            var items = WildcardFilter.Apply(allJobIds, JobSearchBox.Text.Trim());
            JobListBox.ItemsSource = items;
            JobCountLabel.Text = $"{items.Count} of {allJobIds.Count} jobs";
        }
        finally
        {
            _suppressJobListSelection = false;
        }
    }

    private void OnJobSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        PopulateJobList();
    }

    private async void OnJobListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressJobListSelection || !_webViewReady)
            return;

        try
        {
            _suppressExitListSelection = true;
            try { ExitListBox.SelectedItem = null; } finally { _suppressExitListSelection = false; }

            if (JobListBox.SelectedItem is string selectedId)
            {
                var escapedId = JsonSerializer.Serialize(selectedId);
                await GraphWebView.ExecuteScriptAsync($"selectNodeById({escapedId})");
            }
            else
            {
                await GraphWebView.ExecuteScriptAsync("selectNodeById(null)");
            }
        }
        catch (Exception)
        {
            // Non-critical — avoid crash on transient WebView errors.
        }
    }

    private void PopulateExitList()
    {
        _suppressExitListSelection = true;
        try
        {
            var allExits = _currentSnapshot?.Nodes.Values
                               .Select(j => j.Exit)
                               .Where(e => !string.IsNullOrEmpty(e))
                               .Distinct(StringComparer.OrdinalIgnoreCase)
                               .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
                               .ToList()
                           ?? [];

            var items = WildcardFilter.Apply(allExits, ExitSearchBox.Text.Trim());
            ExitListBox.ItemsSource = items;
            ExitCountLabel.Text = $"{items.Count} of {allExits.Count} exits";
        }
        finally
        {
            _suppressExitListSelection = false;
        }
    }

    private void OnExitSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        PopulateExitList();
    }

    private async void OnExitListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressExitListSelection || !_webViewReady)
            return;

        try
        {
            _suppressJobListSelection = true;
            try { JobListBox.SelectedItem = null; } finally { _suppressJobListSelection = false; }

            if (ExitListBox.SelectedItem is string selectedExit)
            {
                var escapedExit = JsonSerializer.Serialize(selectedExit);
                await GraphWebView.ExecuteScriptAsync($"highlightNodesByExit({escapedExit})");
            }
            else
            {
                await GraphWebView.ExecuteScriptAsync("highlightNodesByExit(null)");
            }
        }
        catch (Exception)
        {
            // Non-critical — avoid crash on transient WebView errors.
        }
    }

    private async Task SendGraphToViewAsync(GraphSnapshot snapshot)
    {
        if (!_webViewReady)
            return;

        var graphData = new
        {
            nodes = snapshot.Nodes.Select(n => new
            {
                id = n.Value.Id,
                sn = n.Value.SequenceNumber,
                level = n.Value.Level,
                exit = n.Value.Exit
            }).ToArray(),
            edges = snapshot.Edges.Select(e => new
            {
                source = e.Source,
                target = e.Target
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(graphData);
        var jsArg = JsonSerializer.Serialize(json);
        await GraphWebView.ExecuteScriptAsync($"renderGraph({jsArg})");
    }
}

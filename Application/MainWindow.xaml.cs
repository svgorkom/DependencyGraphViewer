using System.IO;
using System.Text.Json;
using System.Windows;
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
    private bool _isPlaying;
    private double _speedMultiplier = 1.0;
    private double _timeScaleFactor = 1.0;
    private bool _webViewReady;

    public MainWindow()
    {
        InitializeComponent();

        _playbackTimer.Tick += OnPlaybackTick;
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(BasePlaybackIntervalMs);

        Loaded += OnWindowLoaded;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        await GraphWebView.EnsureCoreWebView2Async();

        var webContentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebContent");
        GraphWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.local", webContentPath, CoreWebView2HostResourceAccessKind.Allow);

        GraphWebView.NavigationCompleted += (_, args) =>
        {
            _webViewReady = args.IsSuccess;
        };

        GraphWebView.CoreWebView2.Navigate("https://app.local/graph.html");
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

        _actions = SequencingGraphCsvParser.Parse(dialog.FileName);
        FileNameLabel.Text = Path.GetFileName(dialog.FileName);

        StopPlayback();
        _timeScaleFactor = ComputeTimeScaleFactor(_actions);

        if (_actions.Count > 0)
        {
            TimeSlider.Maximum = _actions.Count - 1;
            TimeSlider.Value = 0;
            TimeSlider.IsEnabled = true;
            PlayPauseButton.IsEnabled = true;
            PlaceholderText.Visibility = Visibility.Collapsed;
            GraphWebView.Visibility = Visibility.Visible;
        }
        else
        {
            TimeSlider.Maximum = 0;
            TimeSlider.IsEnabled = false;
            PlayPauseButton.IsEnabled = false;
            TimestampLabel.Text = "—";
            ActionCountLabel.Text = string.Empty;
            await SendGraphToViewAsync(new GraphSnapshot());
            PlaceholderText.Visibility = Visibility.Visible;
            GraphWebView.Visibility = Visibility.Collapsed;
        }
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

    private void OnSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _speedMultiplier = Math.Pow(2, e.NewValue);
        if (SpeedLabel is not null)
            SpeedLabel.Text = $"{_speedMultiplier:F1}×";
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
        if (_actions is null || _actions.Count == 0)
            return;

        var index = (int)TimeSlider.Value;
        if (index < 0 || index >= _actions.Count)
            return;

        var snapshot = SequencingGraphCsvParser.BuildSnapshot(_actions, index);

        TimestampLabel.Text = _actions[index].Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
        ActionCountLabel.Text = $"Action {index + 1} of {_actions.Count}  ·  {_actions[index].ActionType}";

        await SendGraphToViewAsync(snapshot);
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

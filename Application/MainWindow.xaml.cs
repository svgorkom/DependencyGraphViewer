using System.Windows;
using System.Windows.Threading;
using GraphParser;
using GraphParser.Models;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.WpfGraphControl;
using Microsoft.Win32;
using MsaglColor = Microsoft.Msagl.Drawing.Color;

namespace DependencyGraphViewer;

public partial class MainWindow : Window
{
    private const double MinPlaybackIntervalMs = 80;
    private const double BasePlaybackIntervalMs = 400;

    private static readonly MsaglColor NodeFillColor = new(0x2E, 0x75, 0xB6);
    private static readonly MsaglColor NodeBorderColor = new(0x21, 0x5C, 0x98);
    private static readonly MsaglColor EdgeColor = new(0x95, 0xA5, 0xA6);

    private IReadOnlyList<GraphAction>? _actions;
    private readonly DispatcherTimer _playbackTimer = new();
    private readonly GraphViewer _graphViewer = new();
    private bool _isPlaying;
    private double _speedMultiplier = 1.0;
    private double _timeScaleFactor = 1.0;

    public MainWindow()
    {
        InitializeComponent();
        _graphViewer.BindToPanel(GraphPanel);

        _playbackTimer.Tick += OnPlaybackTick;
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(BasePlaybackIntervalMs);
    }

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            Title = "Open Sequencing Graph Actions CSV"
        };

        if (dialog.ShowDialog() != true)
            return;

        _actions = SequencingGraphCsvParser.Parse(dialog.FileName);
        FileNameLabel.Text = System.IO.Path.GetFileName(dialog.FileName);

        StopPlayback();
        _timeScaleFactor = ComputeTimeScaleFactor(_actions);

        if (_actions.Count > 0)
        {
            TimeSlider.Maximum = _actions.Count - 1;
            TimeSlider.Value = 0;
            TimeSlider.IsEnabled = true;
            PlayPauseButton.IsEnabled = true;
            PlaceholderText.Visibility = Visibility.Collapsed;
        }
        else
        {
            TimeSlider.Maximum = 0;
            TimeSlider.IsEnabled = false;
            PlayPauseButton.IsEnabled = false;
            TimestampLabel.Text = "—";
            ActionCountLabel.Text = string.Empty;
            _graphViewer.Graph = new Graph();
            PlaceholderText.Visibility = Visibility.Visible;
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

    private void UpdateGraph()
    {
        if (_actions is null || _actions.Count == 0)
            return;

        var index = (int)TimeSlider.Value;
        if (index < 0 || index >= _actions.Count)
            return;

        var snapshot = SequencingGraphCsvParser.BuildSnapshot(_actions, index);

        TimestampLabel.Text = _actions[index].Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
        ActionCountLabel.Text = $"Action {index + 1} of {_actions.Count}  ·  {_actions[index].ActionType}";

        _graphViewer.Graph = BuildMsaglGraph(snapshot);
    }

    private static Graph BuildMsaglGraph(GraphSnapshot snapshot)
    {
        var graph = new Graph();
        graph.Attr.LayerDirection = LayerDirection.TB;

        var layoutSettings = (SugiyamaLayoutSettings)graph.LayoutAlgorithmSettings;
        layoutSettings.NodeSeparation = 10;
        layoutSettings.MinNodeWidth = 5;
        layoutSettings.MinNodeHeight = 5;

        foreach (var (nodeId, jobInfo) in snapshot.Nodes)
        {
            var node = graph.AddNode(nodeId);
            node.LabelText = jobInfo.Id;
            node.Attr.FillColor = NodeFillColor;
            node.Attr.Color = NodeBorderColor;
            node.Attr.Shape = Shape.Box;
            node.Attr.XRadius = 4;
            node.Attr.YRadius = 4;
            node.Label.FontColor = MsaglColor.White;
            node.Label.FontSize = 10;

            if (jobInfo.SequenceNumber > 0 || jobInfo.Level > 0 || !string.IsNullOrEmpty(jobInfo.Exit))
            {
                node.LabelText = $"{jobInfo.Id}\nSN:{jobInfo.SequenceNumber} L:{jobInfo.Level}";
            }
        }

        foreach (var edge in snapshot.Edges)
        {
            var msaglEdge = graph.AddEdge(edge.Source, edge.Target);
            msaglEdge.Attr.Color = EdgeColor;
        }

        return graph;
    }
}

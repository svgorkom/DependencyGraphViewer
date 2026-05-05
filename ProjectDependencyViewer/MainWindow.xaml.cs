using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CsprojParser;
using CsprojParser.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace ProjectDependencyViewer;

public partial class MainWindow : Window
{
    private ProjectDependencyGraph? _fullGraph;
    private readonly UserSettings _settings = UserSettings.Load();
    private bool _webViewReady;
    private int _highlightDepth = 1;
    private bool _suppressNodeListSelection;
    private readonly ObservableCollection<string> _excludeFilters = [];

    public MainWindow()
    {
        InitializeComponent();
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

            GraphWebView.CoreWebView2.WebMessageReceived += (_, msgArgs) =>
            {
                Debug.WriteLine($"[WebView2 JS] {msgArgs.TryGetWebMessageAsString()}");
            };

            GraphWebView.NavigationCompleted += async (_, args) =>
            {
                try
                {
                    _webViewReady = args.IsSuccess;
                    Debug.WriteLine($"[WebView2] NavigationCompleted: IsSuccess={args.IsSuccess}, WebErrorStatus={args.WebErrorStatus}");

                    if (_webViewReady
                        && _fullGraph is null
                        && !string.IsNullOrEmpty(_settings.LastFolderPath)
                        && Directory.Exists(_settings.LastFolderPath))
                    {
                        await LoadFolderAsync(_settings.LastFolderPath);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            foreach (var pattern in _settings.ExcludeFilters)
                _excludeFilters.Add(pattern);
            ExcludeFilterList.ItemsSource = _excludeFilters;

            GraphWebView.CoreWebView2.Navigate("https://app.local/graph.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize: {ex.Message}", "Initialization Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnSelectFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select folder containing C# projects"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await LoadFolderAsync(dialog.FolderName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to parse projects: {ex.Message}", "Parse Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadFolderAsync(string folderPath)
    {
        SetLoadingState(true);

        var graph = await Task.Run(() => CsprojDependencyParser.Parse(folderPath));

        _fullGraph = graph;
        FolderPathLabel.Text = folderPath;

        _settings.LastFolderPath = folderPath;
        _settings.Save();

        UpdateFilterCounts();
        PopulateNodeList();

        if (graph.Nodes.Count > 0)
        {
            PlaceholderText.Visibility = Visibility.Collapsed;
            GraphWebView.Visibility = Visibility.Visible;
            ResetViewButton.IsEnabled = true;
            ResetLayoutButton.IsEnabled = true;

            // Allow WPF layout to process the Collapsed→Visible transition so the
            // WebView2 HWND gets its correct dimensions before we push data.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await SendFilteredGraphAsync();
        }
        else
        {
            PlaceholderText.Visibility = Visibility.Visible;
            GraphWebView.Visibility = Visibility.Collapsed;
            ResetViewButton.IsEnabled = false;
            ResetLayoutButton.IsEnabled = false;
            NodeCountLabel.Text = "No projects found";
        }

        SetLoadingState(false);
    }

    private void SetLoadingState(bool isLoading)
    {
        IsEnabled = !isLoading;
        if (isLoading)
            FolderPathLabel.Text = "Scanning projects…";
    }

    private void UpdateFilterCounts()
    {
        if (_fullGraph is null)
            return;

        var projectCount = _fullGraph.Nodes.Values.Count(n => n.NodeType == DependencyType.Project);
        var nugetCount = _fullGraph.Nodes.Values.Count(n => n.NodeType == DependencyType.NuGet);
        var dllCount = _fullGraph.Nodes.Values.Count(n => n.NodeType == DependencyType.Dll);

        ProjectsFilterLabel.Text = $"Projects ({projectCount})";
        NuGetFilterLabel.Text = $"NuGet Packages ({nugetCount})";
        DllFilterLabel.Text = $"DLL References ({dllCount})";
    }

    private async void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_fullGraph is null)
            return;

        PopulateNodeList();
        await SendFilteredGraphAsync();
    }

    private HashSet<DependencyType> GetVisibleTypes()
    {
        var types = new HashSet<DependencyType>();
        if (ShowProjectsCheckBox.IsChecked == true) types.Add(DependencyType.Project);
        if (ShowNuGetCheckBox.IsChecked == true) types.Add(DependencyType.NuGet);
        if (ShowDllCheckBox.IsChecked == true) types.Add(DependencyType.Dll);
        return types;
    }

    private void PopulateNodeList()
    {
        _suppressNodeListSelection = true;
        try
        {
            if (_fullGraph is null)
            {
                NodeListBox.ItemsSource = null;
                NodeCountLabel.Text = string.Empty;
                return;
            }

            var visibleTypes = GetVisibleTypes();

            var allNames = _fullGraph.Nodes.Values
                .Where(n => visibleTypes.Contains(n.NodeType))
                .OrderBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
                .Select(n => n.Id)
                .ToList();

            allNames = WildcardFilter.Exclude(allNames, string.Join(";", _excludeFilters));

            var items = WildcardFilter.Apply(allNames, NodeSearchBox.Text.Trim());
            NodeListBox.ItemsSource = items;
            NodeCountLabel.Text = $"{items.Count} of {allNames.Count} nodes";
        }
        finally
        {
            _suppressNodeListSelection = false;
        }
    }

    private void OnNodeSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        PopulateNodeList();
    }

    private async void OnAddExcludeFilter(object sender, RoutedEventArgs e)
    {
        await AddExcludeFilterAsync();
    }

    private async void OnRemoveExcludeFilter(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string pattern)
        {
            _excludeFilters.Remove(pattern);
            SaveExcludeFilters();
            PopulateNodeList();
            await SendFilteredGraphAsync();
        }
    }

    private async void OnExcludeFilterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await AddExcludeFilterAsync();
            e.Handled = true;
        }
    }

    private async Task AddExcludeFilterAsync()
    {
        var input = ExcludeFilterInput.Text.Trim();
        if (string.IsNullOrEmpty(input))
            return;

        var patterns = input.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in patterns)
        {
            if (!_excludeFilters.Any(f => string.Equals(f, p, StringComparison.OrdinalIgnoreCase)))
                _excludeFilters.Add(p);
        }

        ExcludeFilterInput.Clear();
        SaveExcludeFilters();
        PopulateNodeList();
        await SendFilteredGraphAsync();
    }

    private void SaveExcludeFilters()
    {
        _settings.ExcludeFilters = _excludeFilters.ToList();
        _settings.Save();
    }

    private async void OnNodeListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressNodeListSelection || !_webViewReady)
            return;

        try
        {
            if (NodeListBox.SelectedItem is string selectedId)
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

    private async Task SendFilteredGraphAsync()
    {
        if (_fullGraph is null || !_webViewReady)
        {
            Debug.WriteLine($"[WebView2] SendFilteredGraphAsync skipped: _fullGraph={_fullGraph is not null}, _webViewReady={_webViewReady}");
            return;
        }

        try
        {
            var visibleTypes = GetVisibleTypes();

            var excludeFilter = string.Join(";", _excludeFilters);

            var allIds = _fullGraph.Nodes.Values
                .Where(n => visibleTypes.Contains(n.NodeType))
                .Select(n => n.Id)
                .ToList();

            var keptIds = new HashSet<string>(
                WildcardFilter.Exclude(allIds, excludeFilter),
                StringComparer.OrdinalIgnoreCase);

            var nodes = _fullGraph.Nodes.Values
                .Where(n => keptIds.Contains(n.Id))
                .ToList();

            var nodeIds = new HashSet<string>(nodes.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);

            var edges = _fullGraph.Edges
                .Where(e => nodeIds.Contains(e.Source) && nodeIds.Contains(e.Target))
                .ToList();

            var graphData = new
            {
                nodes = nodes.Select(n => new
                {
                    id = n.Id,
                    name = n.Name,
                    nodeType = n.NodeType.ToString(),
                    version = n.Version ?? string.Empty
                }).ToArray(),
                edges = edges.Select(e => new
                {
                    source = e.Source,
                    target = e.Target,
                    type = e.Type.ToString()
                }).ToArray()
            };

            var json = JsonSerializer.Serialize(graphData);
            var jsArg = JsonSerializer.Serialize(json);
            Debug.WriteLine($"[WebView2] Sending renderGraph with {nodes.Count} nodes, {edges.Count} edges");
            var result = await GraphWebView.ExecuteScriptAsync($"renderGraph({jsArg})");
            Debug.WriteLine($"[WebView2] renderGraph result: {result}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebView2] SendFilteredGraphAsync error: {ex.Message}");
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
        MaxRestoreButton.Content = WindowState == WindowState.Maximized
            ? "\uE923"
            : "\uE922";
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
}

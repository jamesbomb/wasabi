using System.IO;
using System.Windows;
using Microsoft.Win32;
using Wasabi.Core.Diagnostics;
using Wasabi.Core.Engine;
using Wasabi.Core.Persistence;
using Wasabi.Core.Routing;

namespace Wasabi.App;

public partial class MainWindow : Window
{
    private RoutingEngine? _engine;
    private readonly RoutingGraph _graph = new() { Name = "Nuovo patch" };

    public MainWindow()
    {
        InitializeComponent();
        Editor.Graph = _graph;
        Editor.GraphChanged += (_, _) => UpdateStatus();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText.Text = $"{_graph.Nodes.Count} blocchi · {_graph.Connections.Count} collegamenti · {_graph.Name}";
    }

    private void AddAppSource_Click(object sender, RoutedEventArgs e) =>
        Editor.AddNode(NodeType.AppSource, "App", 120, 120);

    private void AddDeviceLoopback_Click(object sender, RoutedEventArgs e) =>
        Editor.AddNode(NodeType.DeviceLoopback, "Loopback", 120, 240);

    private void AddMixer_Click(object sender, RoutedEventArgs e) =>
        Editor.AddNode(NodeType.Mixer, "Mixer", 360, 180);

    private void AddSplitter_Click(object sender, RoutedEventArgs e) =>
        Editor.AddNode(NodeType.Splitter, "Splitter", 360, 320);

    private void AddVirtualBus_Click(object sender, RoutedEventArgs e) =>
        Editor.AddNode(NodeType.VirtualBus, "Bus virtuale", 520, 240);

    private void AddOutput_Click(object sender, RoutedEventArgs e) =>
        Editor.AddNode(NodeType.DeviceOutput, "Uscita", 720, 240);

    private async void StartRouting_Click(object sender, RoutedEventArgs e)
    {
        if (_graph.Nodes.Count == 0)
        {
            MessageBox.Show("Aggiungi almeno un blocco sorgente e uno di uscita.", "WASABI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_graph.Nodes.Any(n => n.Type == NodeType.DeviceOutput))
        {
            MessageBox.Show("Aggiungi almeno un blocco Uscita collegato a un dispositivo audio.", "WASABI", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StopRouting_Click(sender, e);

        _engine = new RoutingEngine(_graph);
        _engine.StatusChanged += (_, msg) => Dispatcher.Invoke(() => StatusText.Text = msg);
        _engine.ErrorOccurred += (_, ex) => Dispatcher.Invoke(() =>
        {
            WasabiLog.Error("Errore segnalato dal motore audio.", ex);
            MessageBox.Show(
                $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Log: {WasabiLog.FilePath}",
                "Errore audio",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        });

        try
        {
            WasabiLog.Info("Richiesto avvio routing dalla UI.");
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            Editor.IsEnabled = false;
            StatusText.Text = "Avvio routing in corso…";

            await Task.Run(() => _engine.Start());
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = "Routing attivo";
        }
        catch (Exception ex)
        {
            WasabiLog.Error("Avvio routing non riuscito.", ex);
            _engine?.Dispose();
            _engine = null;
            StartButton.IsEnabled = true;
            Editor.IsEnabled = true;
            MessageBox.Show(
                $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Log: {WasabiLog.FilePath}",
                "Impossibile avviare",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void StopRouting_Click(object sender, RoutedEventArgs e)
    {
        WasabiLog.Info("Richiesto arresto routing dalla UI.");
        _engine?.Dispose();
        _engine = null;
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        Editor.IsEnabled = true;
        UpdateStatus();
    }

    private void SavePatch_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "WASABI Patch (*.wasabi.json)|*.wasabi.json",
            FileName = $"{_graph.Name}.wasabi.json"
        };
        if (dlg.ShowDialog() != true) return;

        File.WriteAllText(dlg.FileName, PatchSerializer.Serialize(_graph));
        _graph.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
        UpdateStatus();
    }

    private void CalibrateLatency_Click(object sender, RoutedEventArgs e)
    {
        if (_engine?.IsRunning == true)
        {
            MessageBox.Show(
                "Ferma il routing prima di calibrare. I toni di test devono raggiungere una sola uscita alla volta.",
                "Routing attivo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var configuredOutputs = _graph.Nodes.Count(n =>
            n.Type == NodeType.DeviceOutput && !string.IsNullOrWhiteSpace(n.DeviceId));
        if (configuredOutputs < 2)
        {
            MessageBox.Show(
                "Configura almeno due blocchi Uscita prima di calibrare.",
                "Uscite mancanti",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new CalibrationWindow(_graph) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            Editor.Refresh();
            UpdateStatus();
            MessageBox.Show(
                "Ritardi applicati. Avvia il routing per usarli.",
                "Calibrazione completata",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void OpenPatch_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "WASABI Patch (*.wasabi.json)|*.wasabi.json"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            StopRouting_Click(sender, e);
            var loaded = PatchSerializer.Deserialize(File.ReadAllText(dlg.FileName));
            _graph.Nodes.Clear();
            _graph.Connections.Clear();
            _graph.Name = loaded.Name;
            foreach (var node in loaded.Nodes) _graph.Nodes.Add(node);
            foreach (var conn in loaded.Connections) _graph.Connections.Add(conn);
            Editor.Refresh();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            WasabiLog.Error($"Impossibile aprire il patch '{dlg.FileName}'.", ex);
            MessageBox.Show(
                $"Patch non valido o non compatibile:{Environment.NewLine}{ex.Message}",
                "Impossibile aprire patch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _engine?.Dispose();
        base.OnClosed(e);
    }
}

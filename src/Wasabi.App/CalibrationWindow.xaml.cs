using System.Collections.ObjectModel;
using System.Windows;
using Wasabi.Core.Audio;
using Wasabi.Core.Calibration;
using Wasabi.Core.Diagnostics;
using Wasabi.Core.Routing;

namespace Wasabi.App;

public partial class CalibrationWindow : Window
{
    private readonly RoutingGraph _graph;
    private readonly List<RoutingNode> _outputs;
    private readonly ObservableCollection<CalibrationRow> _rows = [];
    private CalibrationResult? _result;
    private CancellationTokenSource? _cancellation;

    public CalibrationWindow(RoutingGraph graph)
    {
        _graph = graph;
        _outputs = graph.Nodes
            .Where(n => n.Type == NodeType.DeviceOutput && !string.IsNullOrWhiteSpace(n.DeviceId))
            .ToList();

        InitializeComponent();
        ResultGrid.ItemsSource = _rows;

        var microphones = AudioDeviceEnumerator.GetCaptureDevices();
        MicrophoneCombo.ItemsSource = microphones;
        MicrophoneCombo.SelectedItem = microphones.FirstOrDefault(d => d.Id == graph.CalibrationMicDeviceId)
                                      ?? microphones.FirstOrDefault();

        RunButton.IsEnabled = _outputs.Count >= 2 && MicrophoneCombo.SelectedItem is not null;
        if (_outputs.Count < 2)
            StatusText.Text = "Configura almeno due blocchi Uscita con un dispositivo selezionato.";
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (MicrophoneCombo.SelectedItem is not AudioDeviceInfo microphone)
        {
            StatusText.Text = "Seleziona un microfono.";
            return;
        }

        _result = null;
        _rows.Clear();
        ApplyButton.IsEnabled = false;
        RunButton.IsEnabled = false;
        CancelTestButton.IsEnabled = true;
        _cancellation = new CancellationTokenSource();
        WasabiLog.Info($"Avvio calibrazione con microfono '{microphone.Name}'.");

        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var calibrator = new OutputDelayCalibrator();
            _result = await Task.Run(() =>
                calibrator.MeasureAsync(_outputs, microphone.Id, progress, _cancellation.Token));

            foreach (var measurement in _result.Measurements)
            {
                _result.SuggestedDelayMsByNodeId.TryGetValue(measurement.NodeId, out var suggestion);
                _rows.Add(new CalibrationRow(measurement, _result.SuggestedDelayMsByNodeId.ContainsKey(measurement.NodeId)
                    ? suggestion
                    : null));
            }

            var validCount = _result.SuggestedDelayMsByNodeId.Count;
            ApplyButton.IsEnabled = validCount >= 2;
            StatusText.Text = validCount >= 2
                ? "Risultati pronti: applica i ritardi proposti o ripeti il test."
                : "Misura non affidabile: aumenta il volume o prova un microfono esterno.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Test interrotto.";
            WasabiLog.Info("Calibrazione interrotta dall'utente.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Calibrazione non riuscita. Consulta il log.";
            WasabiLog.Error("Calibrazione non riuscita.", ex);
            MessageBox.Show(
                $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Log: {WasabiLog.FilePath}",
                "Calibrazione non riuscita",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            RunButton.IsEnabled = _outputs.Count >= 2;
            CancelTestButton.IsEnabled = false;
        }
    }

    private void CancelTest_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null || MicrophoneCombo.SelectedItem is not AudioDeviceInfo microphone)
            return;

        foreach (var output in _outputs)
        {
            if (_result.SuggestedDelayMsByNodeId.TryGetValue(output.Id, out var delay))
                output.OutputDelayMs = delay;
        }

        _graph.CalibrationMicDeviceId = microphone.Id;
        _graph.CalibrationMicDeviceName = microphone.Name;
        WasabiLog.Info("Applicati ritardi della calibrazione automatica.");
        DialogResult = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cancellation?.Cancel();
        base.OnClosed(e);
    }
}

public sealed class CalibrationRow
{
    public string OutputName { get; }
    public string MeasuredText { get; }
    public string SuggestedText { get; }
    public string Status { get; }

    public CalibrationRow(OutputCalibrationMeasurement measurement, int? suggestedDelay)
    {
        OutputName = measurement.OutputName;
        MeasuredText = measurement.MeasuredLatencyMs is double latency ? $"{latency:F1} ms" : "—";
        SuggestedText = suggestedDelay is int delay ? $"+{delay} ms" : "—";
        Status = measurement.Warning ?? $"OK ({measurement.ValidRuns}/3, conf. {measurement.Confidence:F1})";
    }
}

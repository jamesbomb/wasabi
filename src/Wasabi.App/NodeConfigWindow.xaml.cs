using System.Windows;
using Wasabi.Core.Audio;
using Wasabi.Core.Routing;

namespace Wasabi.App;

public partial class NodeConfigWindow : Window
{
    private readonly RoutingNode _node;

    public NodeConfigWindow(RoutingNode node)
    {
        _node = node;
        InitializeComponent();
        TitleBox.Text = node.Title;
        VolumeSlider.Value = node.Volume;
        MuteCheck.IsChecked = node.Muted;
        VolumeLabel.Text = $"{node.Volume:P0}";

        VolumeSlider.ValueChanged += (_, _) =>
            VolumeLabel.Text = $"{VolumeSlider.Value:P0}";

        switch (node.Type)
        {
            case NodeType.AppSource:
                AppPanel.Visibility = Visibility.Visible;
                ProcessCombo.ItemsSource = AudioDeviceEnumerator.GetAudioProcesses();
                if (node.ProcessId is int pid)
                {
                    ProcessCombo.SelectedItem = ProcessCombo.Items.Cast<ProcessInfo>()
                        .FirstOrDefault(p => p.Id == pid);
                }
                break;

            case NodeType.DeviceLoopback:
            case NodeType.DeviceOutput:
                DevicePanel.Visibility = Visibility.Visible;
                DeviceCombo.ItemsSource = AudioDeviceEnumerator.GetRenderDevices();
                if (!string.IsNullOrEmpty(node.DeviceId))
                {
                    DeviceCombo.SelectedItem = DeviceCombo.Items.Cast<AudioDeviceInfo>()
                        .FirstOrDefault(d => d.Id == node.DeviceId);
                }
                break;

            case NodeType.Mixer:
                MixerPanel.Visibility = Visibility.Visible;
                InputCountSlider.Value = node.InputCount;
                break;

            case NodeType.Splitter:
                SplitterPanel.Visibility = Visibility.Visible;
                OutputCountSlider.Value = node.OutputCount;
                break;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _node.Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? _node.Title : TitleBox.Text.Trim();
        _node.Volume = (float)VolumeSlider.Value;
        _node.Muted = MuteCheck.IsChecked == true;

        if (ProcessCombo.SelectedItem is ProcessInfo proc)
        {
            _node.ProcessId = proc.Id;
            _node.ProcessName = proc.DisplayName;
        }

        if (DeviceCombo.SelectedItem is AudioDeviceInfo dev)
        {
            _node.DeviceId = dev.Id;
            _node.DeviceName = dev.Name;
        }

        if (_node.Type == NodeType.Mixer)
            _node.InputCount = (int)InputCountSlider.Value;

        if (_node.Type == NodeType.Splitter)
            _node.OutputCount = (int)OutputCountSlider.Value;

        DialogResult = true;
    }
}

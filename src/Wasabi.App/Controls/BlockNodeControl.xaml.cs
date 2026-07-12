using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wasabi.Core.Routing;

namespace Wasabi.App.Controls;

public partial class BlockNodeControl : UserControl
{
    public RoutingNode Node { get; }

    public event EventHandler<RoutingNode>? ConfigureRequested;
    public event EventHandler<RoutingNode>? DeleteRequested;
    public event EventHandler<PortClickedEventArgs>? PortClicked;

    public BlockNodeControl(RoutingNode node)
    {
        Node = node;
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        TypeBadge.Text = Node.Type switch
        {
            NodeType.AppSource => "APP",
            NodeType.DeviceLoopback => "LOOP",
            NodeType.Mixer => "MIX",
            NodeType.Splitter => "SPLIT",
            NodeType.VirtualBus => "BUS",
            NodeType.DeviceOutput => "OUT",
            _ => "?"
        };

        TitleText.Text = Node.Title;
        SubtitleText.Text = Node.Type switch
        {
            NodeType.AppSource => Node.ProcessName ?? "Seleziona applicazione…",
            NodeType.DeviceLoopback => Node.DeviceName ?? "Seleziona dispositivo…",
            NodeType.DeviceOutput => FormatOutputSubtitle(),
            NodeType.Mixer => $"{Node.InputCount} ingressi",
            NodeType.Splitter => $"{Node.OutputCount} uscite",
            NodeType.VirtualBus => "Bus interno di routing",
            _ => ""
        };

        PortList.ItemsSource = null;
        PortList.ItemsSource = Node.Ports;
    }

    private string FormatOutputSubtitle()
    {
        var device = Node.DeviceName ?? "Seleziona dispositivo…";
        return Node.OutputDelayMs > 0 ? $"{device} · +{Node.OutputDelayMs} ms" : device;
    }

    public Point GetPortCenter(string portId, UIElement relativeTo)
    {
        foreach (var item in PortList.Items)
        {
            if (item is not Port port || port.Id != portId) continue;
            var container = PortList.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
            if (container is null) continue;

            var ellipse = FindVisualChild<System.Windows.Shapes.Ellipse>(container);
            if (ellipse is null) return new Point(Node.X + ActualWidth / 2, Node.Y + ActualHeight / 2);

            var center = ellipse.TransformToAncestor(relativeTo).Transform(new Point(ellipse.ActualWidth / 2, ellipse.ActualHeight / 2));
            return center;
        }

        return TransformToAncestor(relativeTo).Transform(new Point(ActualWidth / 2, ActualHeight / 2));
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindVisualChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    private void Configure_Click(object sender, RoutedEventArgs e) =>
        ConfigureRequested?.Invoke(this, Node);

    private void Delete_Click(object sender, RoutedEventArgs e) =>
        DeleteRequested?.Invoke(this, Node);

    private void Port_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string portId)
            RaisePort(portId);
    }

    private void PortEllipse_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string portId)
            RaisePort(portId);
        e.Handled = true;
    }

    private void RaisePort(string portId)
    {
        var port = Node.Ports.FirstOrDefault(p => p.Id == portId);
        if (port is null) return;
        PortClicked?.Invoke(this, new PortClickedEventArgs { Port = port });
    }
}

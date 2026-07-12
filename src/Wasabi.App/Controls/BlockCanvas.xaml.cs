using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Wasabi.Core.Routing;

namespace Wasabi.App.Controls;

public partial class BlockCanvas : UserControl
{
    public RoutingGraph Graph { get; set; } = new();
    public event EventHandler? GraphChanged;

    private readonly Dictionary<string, BlockNodeControl> _nodeControls = new();
    private string? _pendingSourcePortId;
    private Point? _dragStart;
    private BlockNodeControl? _dragNode;
    private Polyline? _tempWire;
    private readonly List<Polyline> _wires = [];

    public BlockCanvas()
    {
        InitializeComponent();
        SizeChanged += (_, _) => RedrawWires();
    }

    public void AddNode(NodeType type, string title, double x, double y)
    {
        var node = Graph.AddNode(type, title, x, y);
        CreateNodeControl(node);
        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Refresh()
    {
        Surface.Children.Clear();
        _nodeControls.Clear();
        _wires.Clear();
        _tempWire = null;
        _pendingSourcePortId = null;

        foreach (var node in Graph.Nodes)
            CreateNodeControl(node);
        RedrawWires();
    }

    private void CreateNodeControl(RoutingNode node)
    {
        var control = new BlockNodeControl(node);
        control.ConfigureRequested += (_, n) => OpenConfig(n);
        control.DeleteRequested += (_, n) =>
        {
            Graph.RemoveNode(n.Id);
            Refresh();
            GraphChanged?.Invoke(this, EventArgs.Empty);
        };
        control.PortClicked += OnPortClicked;

        Canvas.SetLeft(control, node.X);
        Canvas.SetTop(control, node.Y);
        Canvas.SetZIndex(control, 10);
        Surface.Children.Add(control);
        _nodeControls[node.Id] = control;

        control.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                OpenConfig(node);
                e.Handled = true;
                return;
            }

            _dragNode = control;
            _dragStart = e.GetPosition(Surface);
            control.CaptureMouse();
        };
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragNode is not null && _dragStart is Point start && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(Surface);
            _dragNode.Node.X = Math.Max(0, _dragNode.Node.X + (pos.X - start.X));
            _dragNode.Node.Y = Math.Max(0, _dragNode.Node.Y + (pos.Y - start.Y));
            Canvas.SetLeft(_dragNode, _dragNode.Node.X);
            Canvas.SetTop(_dragNode, _dragNode.Node.Y);
            _dragStart = pos;
            RedrawWires();
            return;
        }

        if (_pendingSourcePortId is not null)
            UpdateTempWire(e.GetPosition(Surface));
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragNode is not null)
        {
            _dragNode.ReleaseMouseCapture();
            _dragNode = null;
            _dragStart = null;
        }
    }

    private void OnPortClicked(object? sender, PortClickedEventArgs e)
    {
        if (e.Port.Direction == PortDirection.Output)
        {
            _pendingSourcePortId = e.Port.Id;
            EnsureTempWire();
            return;
        }

        if (_pendingSourcePortId is null) return;

        var conn = Graph.Connect(_pendingSourcePortId, e.Port.Id);
        _pendingSourcePortId = null;
        RemoveTempWire();
        if (conn is not null)
        {
            RedrawWires();
            GraphChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void EnsureTempWire()
    {
        RemoveTempWire();
        _tempWire = new Polyline
        {
            Stroke = (Brush)FindResource("Accent"),
            StrokeThickness = 2,
            StrokeDashArray = [4, 3]
        };
        Canvas.SetZIndex(_tempWire, 1);
        Surface.Children.Add(_tempWire);
    }

    private void RemoveTempWire()
    {
        if (_tempWire is null) return;
        Surface.Children.Remove(_tempWire);
        _tempWire = null;
    }

    private void UpdateTempWire(Point mouse)
    {
        if (_tempWire is null || _pendingSourcePortId is null) return;
        if (!TryGetPortCenter(_pendingSourcePortId, out var start)) return;
        _tempWire.Points = [start, mouse];
    }

    private void RedrawWires()
    {
        foreach (var wire in _wires)
            Surface.Children.Remove(wire);
        _wires.Clear();

        foreach (var conn in Graph.Connections)
        {
            if (!TryGetPortCenter(conn.SourcePortId, out var a) ||
                !TryGetPortCenter(conn.TargetPortId, out var b)) continue;

            var wire = new Polyline
            {
                Stroke = (Brush)FindResource("Wire"),
                StrokeThickness = 2.5,
                Points = CreateBezier(a, b)
            };
            Canvas.SetZIndex(wire, 1);
            Surface.Children.Add(wire);
            _wires.Add(wire);
        }

        if (_tempWire is not null)
            Surface.Children.Add(_tempWire);
    }

    private static PointCollection CreateBezier(Point a, Point b)
    {
        var dx = Math.Max(40, Math.Abs(b.X - a.X) * 0.5);
        var c1 = new Point(a.X + dx, a.Y);
        var c2 = new Point(b.X - dx, b.Y);
        var points = new PointCollection();
        for (var t = 0.0; t <= 1.0; t += 0.04)
        {
            var u = 1 - t;
            points.Add(new Point(
                u * u * u * a.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * b.X,
                u * u * u * a.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * b.Y));
        }
        return points;
    }

    private bool TryGetPortCenter(string portId, out Point center)
    {
        center = default;
        var port = Graph.FindPort(portId);
        if (port is null) return false;
        if (!_nodeControls.TryGetValue(port.NodeId, out var nodeControl)) return false;
        center = nodeControl.GetPortCenter(portId, Surface);
        return true;
    }

    private void OpenConfig(RoutingNode node)
    {
        var dlg = new NodeConfigWindow(node) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            node.RebuildPorts();
            Refresh();
            GraphChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class PortClickedEventArgs : EventArgs
{
    public required Port Port { get; init; }
}

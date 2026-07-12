namespace Wasabi.Core.Routing;

public sealed class RoutingNode
{
    public required string Id { get; init; }
    public required NodeType Type { get; init; }
    public required string Title { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public float Volume { get; set; } = 1f;
    public bool Muted { get; set; }

    // AppSource
    public int? ProcessId { get; set; }
    public string? ProcessName { get; set; }

    // DeviceLoopback / DeviceOutput
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    // Extra delay applied only to this physical output. Default keeps legacy behavior.
    public int OutputDelayMs { get; set; }

    // Mixer / Splitter
    public int InputCount { get; set; } = 2;
    public int OutputCount { get; set; } = 2;

    public List<Port> Ports { get; } = [];

    public void RebuildPorts()
    {
        Ports.Clear();

        switch (Type)
        {
            case NodeType.AppSource:
            case NodeType.DeviceLoopback:
                Ports.Add(CreatePort(PortDirection.Output, "out", "Audio", 0));
                break;

            case NodeType.Mixer:
                for (var i = 0; i < InputCount; i++)
                    Ports.Add(CreatePort(PortDirection.Input, $"in{i}", $"In {i + 1}", i));
                Ports.Add(CreatePort(PortDirection.Output, "out", "Out", 0));
                break;

            case NodeType.Splitter:
                Ports.Add(CreatePort(PortDirection.Input, "in", "In", 0));
                for (var i = 0; i < OutputCount; i++)
                    Ports.Add(CreatePort(PortDirection.Output, $"out{i}", $"Out {i + 1}", i));
                break;

            case NodeType.VirtualBus:
                Ports.Add(CreatePort(PortDirection.Input, "in", "In", 0));
                Ports.Add(CreatePort(PortDirection.Output, "out", "Out", 0));
                break;

            case NodeType.DeviceOutput:
                Ports.Add(CreatePort(PortDirection.Input, "in", "In", 0));
                break;
        }
    }

    private Port CreatePort(PortDirection direction, string suffix, string label, int index) =>
        new()
        {
            Id = $"{Id}:{suffix}",
            NodeId = Id,
            Direction = direction,
            Label = label,
            Index = index
        };
}

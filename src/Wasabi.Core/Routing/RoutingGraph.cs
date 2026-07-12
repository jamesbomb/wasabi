namespace Wasabi.Core.Routing;

public sealed class RoutingGraph
{
    public string Name { get; set; } = "Patch";
    public List<RoutingNode> Nodes { get; } = [];
    public List<Connection> Connections { get; } = [];

    public RoutingNode AddNode(NodeType type, string title, double x, double y)
    {
        var node = new RoutingNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            Title = title,
            X = x,
            Y = y,
            InputCount = type == NodeType.Mixer ? 4 : 2,
            OutputCount = type == NodeType.Splitter ? 2 : 2
        };
        node.RebuildPorts();
        Nodes.Add(node);
        return node;
    }

    public Connection? Connect(string sourcePortId, string targetPortId)
    {
        var source = FindPort(sourcePortId);
        var target = FindPort(targetPortId);
        if (source is null || target is null) return null;
        if (source.Direction != PortDirection.Output || target.Direction != PortDirection.Input) return null;
        if (Connections.Any(c => c.TargetPortId == targetPortId)) return null;

        var connection = new Connection
        {
            Id = Guid.NewGuid().ToString("N"),
            SourcePortId = sourcePortId,
            TargetPortId = targetPortId
        };
        Connections.Add(connection);
        return connection;
    }

    public void Disconnect(string connectionId) =>
        Connections.RemoveAll(c => c.Id == connectionId);

    public void RemoveNode(string nodeId)
    {
        Connections.RemoveAll(c =>
            c.SourcePortId.StartsWith($"{nodeId}:", StringComparison.Ordinal) ||
            c.TargetPortId.StartsWith($"{nodeId}:", StringComparison.Ordinal));
        Nodes.RemoveAll(n => n.Id == nodeId);
    }

    public Port? FindPort(string portId)
    {
        foreach (var node in Nodes)
        {
            var port = node.Ports.FirstOrDefault(p => p.Id == portId);
            if (port is not null) return port;
        }
        return null;
    }

    public RoutingNode? FindNode(string nodeId) =>
        Nodes.FirstOrDefault(n => n.Id == nodeId);
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Wasabi.Core.Routing;

namespace Wasabi.Core.Persistence;

public static class PatchSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static PatchSerializer()
    {
        // Patch files are intended to stay human-readable and the included
        // examples store node types such as "AppSource" as strings.
        Options.Converters.Add(new JsonStringEnumConverter());
    }

    public static string Serialize(RoutingGraph graph) =>
        JsonSerializer.Serialize(new PatchDto
        {
            Name = graph.Name,
            Nodes = graph.Nodes.Select(n => new NodeDto
            {
                Id = n.Id,
                Type = n.Type,
                Title = n.Title,
                X = n.X,
                Y = n.Y,
                Volume = n.Volume,
                Muted = n.Muted,
                ProcessId = n.ProcessId,
                ProcessName = n.ProcessName,
                DeviceId = n.DeviceId,
                DeviceName = n.DeviceName,
                InputCount = n.InputCount,
                OutputCount = n.OutputCount
            }).ToList(),
            Connections = graph.Connections.Select(c => new ConnectionDto
            {
                Id = c.Id,
                SourcePortId = c.SourcePortId,
                TargetPortId = c.TargetPortId
            }).ToList()
        }, Options);

    public static RoutingGraph Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<PatchDto>(json, Options)
                  ?? throw new InvalidOperationException("Patch non valido.");

        var graph = new RoutingGraph { Name = dto.Name };
        foreach (var nodeDto in dto.Nodes)
        {
            var node = new RoutingNode
            {
                Id = nodeDto.Id,
                Type = nodeDto.Type,
                Title = nodeDto.Title,
                X = nodeDto.X,
                Y = nodeDto.Y,
                Volume = nodeDto.Volume,
                Muted = nodeDto.Muted,
                ProcessId = nodeDto.ProcessId,
                ProcessName = nodeDto.ProcessName,
                DeviceId = nodeDto.DeviceId,
                DeviceName = nodeDto.DeviceName,
                InputCount = nodeDto.InputCount,
                OutputCount = nodeDto.OutputCount
            };
            node.RebuildPorts();
            graph.Nodes.Add(node);
        }

        foreach (var conn in dto.Connections)
        {
            graph.Connections.Add(new Connection
            {
                Id = conn.Id,
                SourcePortId = conn.SourcePortId,
                TargetPortId = conn.TargetPortId
            });
        }

        return graph;
    }

    private sealed class PatchDto
    {
        public string Name { get; set; } = "Patch";
        public List<NodeDto> Nodes { get; set; } = [];
        public List<ConnectionDto> Connections { get; set; } = [];
    }

    private sealed class NodeDto
    {
        public string Id { get; set; } = "";
        public NodeType Type { get; set; }
        public string Title { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public float Volume { get; set; } = 1f;
        public bool Muted { get; set; }
        public int? ProcessId { get; set; }
        public string? ProcessName { get; set; }
        public string? DeviceId { get; set; }
        public string? DeviceName { get; set; }
        public int InputCount { get; set; } = 2;
        public int OutputCount { get; set; } = 2;
    }

    private sealed class ConnectionDto
    {
        public string Id { get; set; } = "";
        public string SourcePortId { get; set; } = "";
        public string TargetPortId { get; set; } = "";
    }
}

namespace Wasabi.Core.Routing;

public sealed class Port
{
    public required string Id { get; init; }
    public required string NodeId { get; init; }
    public required PortDirection Direction { get; init; }
    public required string Label { get; init; }
    public int Index { get; init; }
}

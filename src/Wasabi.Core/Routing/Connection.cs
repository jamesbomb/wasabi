namespace Wasabi.Core.Routing;

public sealed class Connection
{
    public required string Id { get; init; }
    public required string SourcePortId { get; init; }
    public required string TargetPortId { get; init; }
}

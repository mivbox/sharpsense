namespace SharpSense.Application.Context360.Models;

public sealed record Context360Result(
    Context360Node TargetNode,
    Context360RelatedNode[] Callers,
    Context360RelatedNode[] Implementers,
    Context360RelatedNode[] Callees,
    Context360RelatedNode[] Inherits,
    Context360RelatedNode[] Parents,
    Context360RelatedNode[] Children);

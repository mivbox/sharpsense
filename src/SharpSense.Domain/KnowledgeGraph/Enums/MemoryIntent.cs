namespace SharpSense.Domain.KnowledgeGraph.Enums;

/// <summary>
/// Classifies the human or AI intent of a persistent semantic memory. The agent can filter retrievals by intent
/// so a <see cref="Warning"/> survives an unrelated <see cref="Todo"/> sweep, and a <see cref="Decision"/>
/// can be queried separately from a <see cref="Convention"/>.
/// </summary>
public enum MemoryIntent
{
    /// <summary>Project convention; "we always do X this way".</summary>
    Convention = 0,

    /// <summary>Inviolable invariant; "this method MUST do X".</summary>
    Invariant = 1,

    /// <summary>Outstanding work the agent should pick up later.</summary>
    Todo = 2,

    /// <summary>Warning or risk; "be careful, X can happen".</summary>
    Warning = 3,

    /// <summary>Architectural decision; "we chose X over Y because Z".</summary>
    Decision = 4
}

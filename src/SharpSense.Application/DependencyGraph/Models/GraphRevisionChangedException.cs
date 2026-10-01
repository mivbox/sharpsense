namespace SharpSense.Application.DependencyGraph.Models;

public sealed class GraphRevisionChangedException()
    : InvalidOperationException("The workspace graph changed while loading. Restart graph loading to use the latest index.");

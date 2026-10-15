namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IModuleExportName : INode, IDeclarationName
{
    string? Text { get; }
}

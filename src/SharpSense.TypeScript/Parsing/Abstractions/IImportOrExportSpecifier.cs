namespace SharpSense.TypeScript.Parsing.Abstractions;


public interface IImportOrExportSpecifier : INamedDeclaration
{
    IModuleExportName? PropertyName { get; }

    bool IsTypeOnly { get; }
}

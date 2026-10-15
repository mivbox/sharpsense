namespace SharpSense.TypeScript.Parsing.Abstractions;

public interface IClassLikeDeclaration : INamedDeclaration, IObjectTypeDeclaration, IDeclarationWithTypeParameterChildren
{
    NodeArray<HeritageClause>? HeritageClauses { get; }

    NodeArray<IClassElement>? Members { get; }
}

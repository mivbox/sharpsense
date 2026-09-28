namespace SharpSense.Infrastructure.Persistence.Records;

internal sealed class DirectoryClosureRecord
{
    public int AncestorDirectoryId
    {
        get; set;
    }

    public int DescendantDirectoryId
    {
        get; set;
    }

    public int Depth
    {
        get; set;
    }
}

namespace SharpSense.Infrastructure.Persistence.Records;

internal sealed class ProjectNodeRecord
{
    public int Id
    {
        get; set;
    }

    public string Name
    {
        get; set;
    } = string.Empty;

    public int ProjectDocumentId
    {
        get; set;
    }

    public string ContentHash
    {
        get; set;
    } = string.Empty;
}

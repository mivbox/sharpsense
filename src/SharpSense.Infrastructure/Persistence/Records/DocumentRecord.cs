namespace SharpSense.Infrastructure.Persistence.Records;

internal sealed class DocumentRecord
{
    public int Id
    {
        get; set;
    }

    public int DirectoryId
    {
        get; set;
    }

    public string FileName
    {
        get; set;
    } = string.Empty;

    public string Extension
    {
        get; set;
    } = string.Empty;

    public string RelativePath
    {
        get; set;
    } = string.Empty;

    public DocumentKind Kind
    {
        get; set;
    } = DocumentKind.Other;
}

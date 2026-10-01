namespace SharpSense.Infrastructure.Persistence.Records;

internal sealed class DirectoryRecord
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

using System.Collections;

namespace SharpSense.TypeScript.Parsing.Models;

/// <summary>A parser-owned sequence with source bounds and trailing-comma information.</summary>
public sealed class NodeArray<T> : TextRange, IReadOnlyList<T>
{
    private readonly List<T> _items;

    public NodeArray()
    {
        _items = [];
    }

    public NodeArray(IEnumerable<T> elements)
    {
        _items = [.. elements];
    }

    public int Count => _items.Count;

    public T this[int index] => _items[index];

    public bool HasTrailingComma { get; internal set; }

    public bool IsMissingList { get; internal set; }

    internal void Add(T item) => _items.Add(item);

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

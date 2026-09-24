using AwesomeAssertions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Indexing.TypeScript;
using TreeSitter;

namespace SharpSense.Infrastructure.Tests.Indexing.TypeScript;

public sealed class HttpEdgeExtractionPassTests
{
    [Theory]
    [InlineData("fetch('/items')", "GET")]
    [InlineData("fetch('/items', {})", "GET")]
    [InlineData("fetch('/items', { headers })", "GET")]
    [InlineData("fetch('/items', { headers: { method: 'POST' } })", "GET")]
    [InlineData("fetch('/items', { /* method: 'DELETE' */ headers: {} })", "GET")]
    [InlineData("fetch('/items', { // method: 'DELETE'\n headers: {} })", "GET")]
    [InlineData("fetch('/items', { headers: { method: 'GET' }, method: 'post' })", "POST")]
    [InlineData("fetch('/items', { method: /* request verb */ 'PATCH', headers: { method: 'DELETE' } })", "PATCH")]
    [InlineData("fetch('/items', { 'method': 'put' })", "PUT")]
    [InlineData("fetch('/items', { method: 'POST', method: 'DELETE' })", "DELETE")]
    [InlineData("fetch('/items', { method: 'OPTIONS', headers() { return { method: 'POST' }; } })", "OPTIONS")]
    [InlineData("fetch(/* URL */ '/items', /* init */ { method: 'POST' })", "POST")]
    [InlineData("axios.post(/* URL */ '/items', {})", "POST")]
    [InlineData("axios.get('/items')", "GET")]
    public void WhenRequestMethodIsStaticallyKnown_ThenUsesOnlyActualTopLevelMethod(string expression, string method)
    {
        var edges = Extract(expression);

        edges.Should().ContainSingle();
        edges[0].CallerId.Should().Be("code:ts:client.ts:request");
        edges[0].CalleeId.Should().Be($"http:{method}:%2Fitems");
        edges[0].EdgeType.Should().Be(EdgeType.HttpRequest);
        edges[0].Metadata.Should().Be("/items");
    }

    [Theory]
    [InlineData("fetch('/items', options)")]
    [InlineData("fetch('/items', getOptions())")]
    [InlineData("fetch('/items', { method: verb })")]
    [InlineData("fetch('/items', { method })")]
    [InlineData("fetch('/items', { /* method: 'POST' */ method: verb })")]
    [InlineData("fetch('/items', { ...options })")]
    [InlineData("fetch('/items', { method: 'POST', ...options })")]
    [InlineData("fetch('/items', { ...options, method: 'POST' })")]
    [InlineData("fetch('/items', { [verb]: 'POST' })")]
    [InlineData("fetch('/items', { ['method']: verb })")]
    [InlineData("fetch('/items', { get method() { return verb; } })")]
    [InlineData("fetch('/items', Object.assign({}, options))")]
    [InlineData("fetch('/items', { __proto__: options })")]
    [InlineData("fetch('/items', { 'm\\u0065thod': verb })")]
    [InlineData("fetch('/items', { m\\u0065thod: verb })")]
    [InlineData("fetch('/items', { method: 'POST' + verb + 'GET' })")]
    [InlineData("fetch(url, { method: 'POST' })")]
    [InlineData("fetch('/items' + suffix + '/other', { method: 'POST' })")]
    public void WhenOptionsOrUrlAreUnknown_ThenDoesNotInventHttpDependency(string expression)
        => Extract(expression).Should().BeEmpty();

    private static IReadOnlyList<IndexedDependency> Extract(string expression)
    {
        var source = $"export async function request(options, verb, method, headers, url, suffix) {{ return {expression}; }}";
        using var language = new Language("TypeScript");
        using var parser = new Parser(language);
        using var file = new TypeScriptParsedFile(new DiscoveredFile("/repo/client.ts", "client.ts"), source,
            parser.Parse(source) ?? throw new InvalidOperationException("Fixture did not parse."));
        var context = new TypeScriptPassContext("/repo/tsconfig.json", [file], isIncremental: false);
        new CodeNodeExtractionPass().Execute(context);
        new HttpEdgeExtractionPass().Execute(context);
        return context.Edges.ToArray();
    }
}

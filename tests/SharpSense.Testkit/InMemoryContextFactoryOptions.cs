namespace SharpSense.Testkit;

public sealed record InMemoryContextFactoryOptions(
    bool UseMigrations = false,
    bool LoadVectorExtension = false);

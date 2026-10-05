namespace Cngn.Sdk;

public enum CngnEnvironment
{
    Test,
    Live
}

public sealed record CngnClientOptions
{
    public required string ApiKey { get; init; }
    public required string EncryptionKey { get; init; }
    public required string PrivateKey { get; init; }
    public CngnEnvironment? Environment { get; init; }
    public Uri BaseUri { get; init; } = new("https://api.cngn.co/v1/api/");
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; init; } = 3;
}

public sealed record CngnRequestOptions
{
    public string? IdempotencyKey { get; init; }
}

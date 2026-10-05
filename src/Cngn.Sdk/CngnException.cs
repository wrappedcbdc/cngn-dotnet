using System.Text.Json;

namespace Cngn.Sdk;

public enum CngnErrorKind
{
    Configuration,
    Authentication,
    IpAllowlist,
    Permission,
    RateLimit,
    Validation,
    NotFound,
    ServiceUnavailable,
    Api,
    Network,
    Encryption,
    Decryption
}

public sealed class CngnException : Exception
{
    public CngnErrorKind Kind { get; }
    public int? Status { get; }
    public JsonElement? Response { get; }

    public CngnException(CngnErrorKind kind, string message, int? status = null,
        JsonElement? response = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Status = status;
        Response = response?.Clone();
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cngn.Sdk;

public sealed record WebhookEvent(string Event, JsonElement Data, string? Timestamp);

public static class Webhooks
{
    public static bool Verify(ReadOnlySpan<byte> rawBody, string? signature, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || signature is null
            || !signature.StartsWith("sha256=", StringComparison.Ordinal)) return false;

        ReadOnlySpan<char> hex = signature.AsSpan(7);
        if (hex.Length != 64) return false;
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] key = Encoding.UTF8.GetBytes(secret);
        try
        {
            Span<byte> expected = stackalloc byte[32];
            HMACSHA256.HashData(key, rawBody, expected);
            return CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static WebhookEvent Parse(ReadOnlySpan<byte> rawBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawBody.ToArray());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("event", out JsonElement eventName)
                || eventName.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Object)
                throw new CngnException(CngnErrorKind.Validation, "Invalid webhook payload");

            string? timestamp = root.TryGetProperty("timestamp", out JsonElement value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return new WebhookEvent(eventName.GetString()!, data.Clone(), timestamp);
        }
        catch (JsonException error)
        {
            throw new CngnException(CngnErrorKind.Validation, "Invalid webhook payload", innerException: error);
        }
    }
}

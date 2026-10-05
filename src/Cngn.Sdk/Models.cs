using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cngn.Sdk;

public sealed record Balance
{
    [JsonPropertyName("asset_type")]
    public string? AssetType { get; init; }
    [JsonPropertyName("asset_code")]
    public string? AssetCode { get; init; }
    [JsonPropertyName("balance")]
    public decimal? Amount { get; init; }
}

public sealed record Transaction
{
    public string? Id { get; init; }
    [JsonPropertyName("from")]
    public string? From { get; init; }
    public decimal? Amount { get; init; }
    public string? Description { get; init; }
    public string? Status { get; init; }
    public string? Network { get; init; }
    [JsonPropertyName("trx_ref")]
    public string? Reference { get; init; }
    [JsonPropertyName("trx_type")]
    public string? Type { get; init; }
    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }
    public JsonElement? Receiver { get; init; }
    [JsonPropertyName("asset_type")]
    public string? AssetType { get; init; }
    [JsonPropertyName("asset_symbol")]
    public string? AssetSymbol { get; init; }
    [JsonPropertyName("base_trx_hash")]
    public string? BaseTransactionHash { get; init; }
    [JsonPropertyName("extl_trx_hash")]
    public string? ExternalTransactionHash { get; init; }
    [JsonPropertyName("explorer_link")]
    public string? ExplorerLink { get; init; }
}

public sealed record Pagination
{
    public int Count { get; init; }
    public int Pages { get; init; }
    public bool IsLastPage { get; init; }
    public int? NextPage { get; init; }
    public int? PreviousPage { get; init; }
}

public sealed record TransactionPage
{
    public IReadOnlyList<Transaction> Data { get; init; } = [];
    public Pagination Pagination { get; init; } = new();
}

public sealed record Network
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    [JsonPropertyName("short_name")]
    public string? ShortName { get; init; }
    public bool? IsDisabled { get; init; }
    public JsonElement? Blockchain { get; init; }
}

public record VirtualAccount
{
    public string? AccountNumber { get; init; }
    public string? AccountName { get; init; }
    public string? BankName { get; init; }
    public string? BankCode { get; init; }
}

public sealed record TemporaryAccount : VirtualAccount
{
    public string? Reference { get; init; }
    public string? PaymentReference { get; init; }
    public decimal? Amount { get; init; }
    public decimal? AmountExpected { get; init; }
    public decimal? Fee { get; init; }
    public decimal? Vat { get; init; }
    public string? Currency { get; init; }
    public string? Status { get; init; }
    public string? Narration { get; init; }
    public string? ExpiresAt { get; init; }
}

public sealed record Bank
{
    public string? Name { get; init; }
    public string? Code { get; init; }
    public string? BankCode { get; init; }
}

public sealed record AccountVerification
{
    public string? AccountName { get; init; }
    public string? AccountNumber { get; init; }
    public string? BankCode { get; init; }
}

public sealed record TransferResult
{
    public string? TrxRef { get; init; }
    public string? Address { get; init; }
}

public sealed record BridgeQuote
{
    public decimal? AmountReceivable { get; init; }
    public decimal? NetworkFee { get; init; }
    public decimal? BridgeFee { get; init; }
}

public sealed record BridgeResult
{
    public string? ReceivableAddress { get; init; }
    public string? TransactionId { get; init; }
    public string? Reference { get; init; }
}

public sealed record WhitelistEntry
{
    public string? Id { get; init; }
    public string? NetworkId { get; init; }
    public string? PublicKey { get; init; }
    public string? InternalPublicKey { get; init; }
    public JsonElement? Network { get; init; }
    public string? CreatedAt { get; init; }
    public string? UpdatedAt { get; init; }
}

public sealed record Customer(string Email, string Name);

public sealed record TemporaryAccountRequest(decimal Amount, Customer Customer,
    string AccountName, string? Narration = null);

public sealed record RedeemRequest(decimal Amount, string BankCode,
    string AccountNumber, bool SaveDetails = false);

public sealed record WithdrawRequest(decimal Amount, string Address,
    string NetworkId, bool ShouldSaveAddress = false);

public sealed record BridgeQuoteRequest(decimal Amount, string OriginNetworkId,
    string DestinationNetworkId, string DestinationAddress);

public sealed record BridgeRequest(string OriginNetworkId, string DestinationNetworkId,
    string DestinationAddress, string? SenderAddress = null, string? CallbackUrl = null);

public sealed record BankAccountRequest(string BankName, string BankAccountName,
    string BankAccountNumber);

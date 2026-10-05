using System.Globalization;
using System.Text.Json;

namespace Cngn.Sdk.Tests;

public sealed class LiveMutationTests
{
    [Fact]
    public async Task CreatesTemporaryVirtualAccount()
    {
        CheckEnabled();
        using var client = CreateClient();
        string nonce = Required("CNGN_TEST_TEMPORARY_IDEMPOTENCY_KEY");
        var request = new TemporaryAccountRequest(Amount("CNGN_TEST_TEMP_AMOUNT", 1000),
            new Customer(Required("CNGN_TEST_CUSTOMER_EMAIL"),
                Required("CNGN_TEST_CUSTOMER_NAME")),
            Required("CNGN_TEST_TEMPORARY_ACCOUNT_NAME"), "SDK test");
        TemporaryAccount account = await client.CreateTemporaryVirtualAccountAsync(request,
            new CngnRequestOptions { IdempotencyKey = nonce },
            TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(account.AccountNumber));
        Assert.False(string.IsNullOrWhiteSpace(account.Reference));
    }

    [Fact]
    public async Task UpdatesBankAccount()
    {
        CheckEnabled();
        using var client = CreateClient();
        await client.UpdateBankAccountAsync(new BankAccountRequest(
            Required("CNGN_TEST_BANK_NAME"), Required("CNGN_TEST_BANK_ACCOUNT_NAME"),
            Required("CNGN_TEST_ACCOUNT_NUMBER")), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhitelistsAddress()
    {
        CheckEnabled();
        using var client = CreateClient();
        string address = Required("CNGN_TEST_WALLET_ADDRESS");
        JsonElement? response = await client.WhitelistAddressAsync(Required("CNGN_TEST_NETWORK_ID"),
            address, TestContext.Current.CancellationToken);
        Assert.True(response is { ValueKind: JsonValueKind.Array });
        Assert.Contains(response.Value.EnumerateArray(), entry =>
            entry.TryGetProperty("publicKey", out JsonElement key)
            && string.Equals(key.GetString(), address, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RedeemsAsset()
    {
        CheckEnabled();
        using var client = CreateClient();
        TransferResult result = await client.RedeemAssetAsync(new RedeemRequest(
            Amount("CNGN_TEST_REDEEM_AMOUNT", 1), Required("CNGN_TEST_BANK_CODE"),
            Required("CNGN_TEST_ACCOUNT_NUMBER")),
            new CngnRequestOptions { IdempotencyKey = Required("CNGN_TEST_REDEEM_IDEMPOTENCY_KEY") },
            TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(result.TrxRef));
        Assert.False(string.IsNullOrWhiteSpace(result.Address));
    }

    [Fact]
    public async Task WithdrawsAsset()
    {
        CheckEnabled();
        using var client = CreateClient();
        TransferResult result = await client.WithdrawAsync(new WithdrawRequest(
            Amount("CNGN_TEST_WITHDRAW_AMOUNT", 1), Required("CNGN_TEST_WALLET_ADDRESS"),
            Required("CNGN_TEST_NETWORK_ID")),
            new CngnRequestOptions { IdempotencyKey = Required("CNGN_TEST_WITHDRAW_IDEMPOTENCY_KEY") },
            TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(result.TrxRef));
        Transaction status = await client.VerifyWithdrawalAsync(result.TrxRef,
            TestContext.Current.CancellationToken);
        Assert.Equal(result.TrxRef, status.Reference);
    }

    [Fact]
    public async Task StartsBridge()
    {
        CheckEnabled();
        using var client = CreateClient();
        BridgeResult result = await client.BridgeAsync(new BridgeRequest(
            Required("CNGN_TEST_ORIGIN_NETWORK_ID"),
            Required("CNGN_TEST_DESTINATION_NETWORK_ID"),
            Required("CNGN_TEST_WALLET_ADDRESS")),
            new CngnRequestOptions { IdempotencyKey = Required("CNGN_TEST_BRIDGE_IDEMPOTENCY_KEY") },
            TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(result.ReceivableAddress));
        Assert.False(string.IsNullOrWhiteSpace(result.Reference));
    }

    private static void CheckEnabled()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("CNGN_LIVE_MUTATIONS") == "1",
            "Live mutations are disabled");
        if (!Required("CNGN_API_KEY").StartsWith("cngn_test_", StringComparison.Ordinal))
            throw new InvalidOperationException("Live mutations require a test API key");
    }

    private static decimal Amount(string name, decimal fallback)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text)) return WithinCap(fallback);
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture,
            out decimal value) || value <= 0)
            throw new InvalidOperationException($"{name} must be positive");
        return WithinCap(value);
    }

    private static decimal WithinCap(decimal value)
    {
        if (!decimal.TryParse(Required("CNGN_TEST_MAX_AMOUNT"), NumberStyles.Number,
            CultureInfo.InvariantCulture, out decimal cap) || cap <= 0 || value > cap)
            throw new InvalidOperationException("Test amount exceeds CNGN_TEST_MAX_AMOUNT");
        return value;
    }

    private static CngnClient CreateClient() => new(new CngnClientOptions
    {
        ApiKey = Required("CNGN_API_KEY"),
        EncryptionKey = Required("CNGN_ENCRYPTION_KEY"),
        PrivateKey = File.ReadAllText(Required("CNGN_PRIVATE_KEY_PATH")),
        Environment = CngnEnvironment.Test,
        MaxRetries = 0
    });

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException($"{name} is required for live mutations");
}

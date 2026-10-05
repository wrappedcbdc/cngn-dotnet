using System.Security.Cryptography;
using System.Text;

namespace Cngn.Sdk.Tests;

public sealed class LiveReadOnlyTests
{
    [Theory]
    [InlineData("balance")]
    [InlineData("transactions")]
    [InlineData("transaction-iterator")]
    [InlineData("networks-basic")]
    [InlineData("networks-with-blockchain")]
    [InlineData("virtual-accounts")]
    [InlineData("banks")]
    [InlineData("whitelisted-addresses-basic")]
    [InlineData("whitelisted-addresses-with-network")]
    public async Task ReadsLiveEndpoint(string endpoint)
    {
        Assert.SkipUnless(Enabled(), "Live checks are disabled");
        using var client = CreateClient();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        switch (endpoint)
        {
            case "balance":
                Assert.NotNull(await client.GetBalanceAsync(cancellationToken));
                break;
            case "transactions":
                Assert.NotNull(await client.GetTransactionsAsync(cancellationToken: cancellationToken));
                break;
            case "transaction-iterator":
                await foreach (Transaction _ in client.IterateTransactionsAsync(10, cancellationToken))
                    break;
                break;
            case "networks-basic":
                Assert.NotNull(await client.GetNetworksAsync(cancellationToken: cancellationToken));
                break;
            case "networks-with-blockchain":
                Assert.NotNull(await client.GetNetworksAsync(true, cancellationToken));
                break;
            case "virtual-accounts":
                Assert.NotNull(await client.GetVirtualAccountsAsync(cancellationToken));
                break;
            case "banks":
                Assert.NotNull(await client.GetBanksAsync(cancellationToken));
                break;
            case "whitelisted-addresses-basic":
                Assert.NotNull(await client.GetWhitelistedAddressesAsync(cancellationToken: cancellationToken));
                break;
            case "whitelisted-addresses-with-network":
                Assert.NotNull(await client.GetWhitelistedAddressesAsync(true, cancellationToken));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(endpoint));
        }
    }

    [Fact]
    public async Task VerifiesConfiguredWithdrawal()
    {
        Assert.SkipUnless(Enabled(), "Live checks are disabled");
        string? reference = Environment.GetEnvironmentVariable("CNGN_TEST_WITHDRAWAL_REFERENCE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(reference), "Withdrawal reference is unset");
        using var client = CreateClient();
        Assert.NotNull(await client.VerifyWithdrawalAsync(reference, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VerifiesConfiguredBankAccount()
    {
        Assert.SkipUnless(Enabled(), "Live checks are disabled");
        string? bankCode = Environment.GetEnvironmentVariable("CNGN_TEST_BANK_CODE");
        string? accountNumber = Environment.GetEnvironmentVariable("CNGN_TEST_ACCOUNT_NUMBER");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(bankCode) || string.IsNullOrWhiteSpace(accountNumber),
            "Bank test inputs are unset");
        using var client = CreateClient();
        Assert.NotNull(await client.VerifyBankAccountAsync(bankCode, accountNumber,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetsConfiguredBridgeQuote()
    {
        Assert.SkipUnless(Enabled(), "Live checks are disabled");
        string? amountText = Environment.GetEnvironmentVariable("CNGN_TEST_BRIDGE_AMOUNT");
        string? originNetworkId = Environment.GetEnvironmentVariable("CNGN_TEST_ORIGIN_NETWORK_ID");
        string? destinationNetworkId = Environment.GetEnvironmentVariable("CNGN_TEST_DESTINATION_NETWORK_ID");
        string? destinationAddress = Environment.GetEnvironmentVariable("CNGN_TEST_DESTINATION_ADDRESS");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(amountText)
            || string.IsNullOrWhiteSpace(originNetworkId)
            || string.IsNullOrWhiteSpace(destinationNetworkId)
            || string.IsNullOrWhiteSpace(destinationAddress), "Bridge quote inputs are unset");
        if (!decimal.TryParse(amountText, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out decimal amount) || amount <= 0)
            throw new InvalidOperationException("CNGN_TEST_BRIDGE_AMOUNT must be positive");
        using var client = CreateClient();
        Assert.NotNull(await client.GetBridgeQuoteAsync(new BridgeQuoteRequest(
            amount, originNetworkId!, destinationNetworkId!, destinationAddress!),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void VerifiesConfiguredWebhookSecret()
    {
        Assert.SkipUnless(Enabled(), "Live checks are disabled");
        string secret = Required("CNGN_SIGNING_SECRET");
        byte[] body = "{\"event\":\"sdk.test\",\"data\":{\"reference\":\"test\"}}"u8.ToArray();
        byte[] key = Encoding.UTF8.GetBytes(secret);
        try
        {
            string signature = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(key, body));
            Assert.True(Webhooks.Verify(body, signature, secret));
            Assert.False(Webhooks.Verify("{}"u8, signature, secret));
            Assert.Equal("sdk.test", Webhooks.Parse(body).Event);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static bool Enabled() =>
        Environment.GetEnvironmentVariable("CNGN_LIVE_TESTS") == "1";

    private static CngnClient CreateClient()
    {
        string apiKey = Required("CNGN_API_KEY");
        if (!apiKey.StartsWith("cngn_test_", StringComparison.Ordinal))
            throw new InvalidOperationException("Live checks require a test API key");
        return new CngnClient(new CngnClientOptions
        {
            ApiKey = apiKey,
            EncryptionKey = Required("CNGN_ENCRYPTION_KEY"),
            PrivateKey = File.ReadAllText(Required("CNGN_PRIVATE_KEY_PATH")),
            Environment = CngnEnvironment.Test,
            MaxRetries = 0
        });
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException($"{name} is required for live checks");
}

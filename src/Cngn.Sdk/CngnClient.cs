using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Cngn.Sdk;

public sealed class CngnClient : ICngnClient
{
    private readonly CngnTransport _transport;

    public CngnEnvironment Environment => _transport.Environment;

    public CngnClient(CngnClientOptions options, HttpClient? httpClient = null)
    {
        _transport = new CngnTransport(options, httpClient);
    }

    public Task<IReadOnlyList<Balance>> GetBalanceAsync(CancellationToken cancellationToken = default) =>
        _transport.SendAsync<IReadOnlyList<Balance>>(HttpMethod.Get, "balance", ResponseShape.Array,
            safeToRetry: true, cancellationToken: cancellationToken);

    public Task<TransactionPage> GetTransactionsAsync(int page = 1, int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || limit < 1) throw new ArgumentOutOfRangeException(nameof(page), "Page and limit must be positive");
        return _transport.SendAsync<TransactionPage>(HttpMethod.Get, "transactions", ResponseShape.Object,
            query: new Dictionary<string, string>
            {
                ["page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }, safeToRetry: true, cancellationToken: cancellationToken);
    }

    public async IAsyncEnumerable<Transaction> IterateTransactionsAsync(int limit = 100,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int page = 1; ; page++)
        {
            TransactionPage result = await GetTransactionsAsync(page, limit, cancellationToken).ConfigureAwait(false);
            if (result.Data is null || result.Pagination is null)
                throw new CngnException(CngnErrorKind.Api, "Invalid transaction page");
            foreach (Transaction transaction in result.Data) yield return transaction;
            if (result.Pagination.IsLastPage || result.Data.Count == 0) yield break;
        }
    }

    public Task<IReadOnlyList<Network>> GetNetworksAsync(bool includeBlockchain = false,
        CancellationToken cancellationToken = default) =>
        _transport.SendAsync<IReadOnlyList<Network>>(HttpMethod.Get, "networks", ResponseShape.Array,
            query: includeBlockchain ? new Dictionary<string, string> { ["includeBlockchain"] = "true" } : null,
            safeToRetry: true, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<VirtualAccount>> GetVirtualAccountsAsync(CancellationToken cancellationToken = default) =>
        _transport.SendAsync<IReadOnlyList<VirtualAccount>>(HttpMethod.Get, "virtual-account", ResponseShape.Array,
            safeToRetry: true, cancellationToken: cancellationToken);

    public Task<TemporaryAccount> CreateTemporaryVirtualAccountAsync(TemporaryAccountRequest request,
        CngnRequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Positive(request.Amount);
        ArgumentNullException.ThrowIfNull(request.Customer);
        Required(request.Customer.Email, nameof(request.Customer.Email));
        Required(request.Customer.Name, nameof(request.Customer.Name));
        Required(request.AccountName, nameof(request.AccountName));
        return _transport.SendAsync<TemporaryAccount>(HttpMethod.Post, "virtual-account/temporary",
            ResponseShape.Object, body: request, idempotencyKey: options?.IdempotencyKey,
            cancellationToken: cancellationToken);
    }

    public Task<TransferResult> RedeemAssetAsync(RedeemRequest request,
        CngnRequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Positive(request.Amount);
        Required(request.BankCode, nameof(request.BankCode));
        Required(request.AccountNumber, nameof(request.AccountNumber));
        return _transport.SendAsync<TransferResult>(HttpMethod.Post, "redeemAsset", ResponseShape.Object,
            body: request, idempotencyKey: options?.IdempotencyKey, cancellationToken: cancellationToken);
    }

    public Task<AccountVerification> VerifyBankAccountAsync(string bankCode, string accountNumber,
        CancellationToken cancellationToken = default) =>
        _transport.SendAsync<AccountVerification>(HttpMethod.Post, "account/verify", ResponseShape.Object,
            body: new
            {
                BankCode = Required(bankCode, nameof(bankCode)),
                AccountNumber = Required(accountNumber, nameof(accountNumber))
            },
            safeToRetry: true, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken cancellationToken = default) =>
        _transport.SendAsync<IReadOnlyList<Bank>>(HttpMethod.Get, "banks", ResponseShape.Array,
            safeToRetry: true, cancellationToken: cancellationToken);

    public Task<JsonElement?> UpdateBankAccountAsync(BankAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Required(request.BankName, nameof(request.BankName));
        Required(request.BankAccountName, nameof(request.BankAccountName));
        Required(request.BankAccountNumber, nameof(request.BankAccountNumber));
        return _transport.SendRawAsync(HttpMethod.Put, "bank-account", request, cancellationToken);
    }

    public Task<TransferResult> WithdrawAsync(WithdrawRequest request,
        CngnRequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Positive(request.Amount);
        Required(request.Address, nameof(request.Address));
        Required(request.NetworkId, nameof(request.NetworkId));
        return _transport.SendAsync<TransferResult>(HttpMethod.Post, "withdraw", ResponseShape.Object,
            body: request, idempotencyKey: options?.IdempotencyKey, cancellationToken: cancellationToken);
    }

    public Task<Transaction> VerifyWithdrawalAsync(string reference,
        CancellationToken cancellationToken = default) =>
        _transport.SendAsync<Transaction>(HttpMethod.Get,
            "withdraw/verify/" + WithdrawalReference(reference),
            ResponseShape.Object, safeToRetry: true, cancellationToken: cancellationToken);

    public Task<BridgeQuote> GetBridgeQuoteAsync(BridgeQuoteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Positive(request.Amount);
        Required(request.OriginNetworkId, nameof(request.OriginNetworkId));
        Required(request.DestinationNetworkId, nameof(request.DestinationNetworkId));
        Required(request.DestinationAddress, nameof(request.DestinationAddress));
        return _transport.SendAsync<BridgeQuote>(HttpMethod.Post, "bridge-quote", ResponseShape.Object,
            body: request, safeToRetry: true, cancellationToken: cancellationToken);
    }

    public Task<BridgeResult> BridgeAsync(BridgeRequest request,
        CngnRequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Required(request.OriginNetworkId, nameof(request.OriginNetworkId));
        Required(request.DestinationNetworkId, nameof(request.DestinationNetworkId));
        Required(request.DestinationAddress, nameof(request.DestinationAddress));
        return _transport.SendAsync<BridgeResult>(HttpMethod.Post, "bridge", ResponseShape.Object,
            body: request, idempotencyKey: options?.IdempotencyKey, cancellationToken: cancellationToken);
    }

    public Task<JsonElement?> WhitelistAddressAsync(string networkId, string address,
        CancellationToken cancellationToken = default) =>
        _transport.SendRawAsync(HttpMethod.Post, "whitelist",
            new
            {
                NetworkId = Required(networkId, nameof(networkId)),
                Address = Required(address, nameof(address))
            }, cancellationToken);

    public Task<IReadOnlyList<WhitelistEntry>> GetWhitelistedAddressesAsync(bool includeNetwork = false,
        CancellationToken cancellationToken = default) =>
        _transport.SendAsync<IReadOnlyList<WhitelistEntry>>(HttpMethod.Get, "whitelisted", ResponseShape.Array,
            query: includeNetwork ? new Dictionary<string, string> { ["includeNetwork"] = "true" } : null,
            safeToRetry: true, cancellationToken: cancellationToken);

    public void Dispose() => _transport.Dispose();

    private static decimal Positive(decimal amount) =>
        amount <= 0 ? throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive") : amount;

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required", name) : value;

    private static string WithdrawalReference(string reference)
    {
        Required(reference, nameof(reference));
        if (reference is "." or "..")
            throw new ArgumentException("Invalid withdrawal reference", nameof(reference));
        return Uri.EscapeDataString(reference);
    }
}

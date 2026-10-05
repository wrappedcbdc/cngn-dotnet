using System.Text.Json;

namespace Cngn.Sdk;

public interface ICngnClient : IDisposable
{
    CngnEnvironment Environment { get; }
    Task<IReadOnlyList<Balance>> GetBalanceAsync(CancellationToken cancellationToken = default);
    Task<TransactionPage> GetTransactionsAsync(int page = 1, int limit = 10,
        CancellationToken cancellationToken = default);
    IAsyncEnumerable<Transaction> IterateTransactionsAsync(int limit = 100,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Network>> GetNetworksAsync(bool includeBlockchain = false,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VirtualAccount>> GetVirtualAccountsAsync(CancellationToken cancellationToken = default);
    Task<TemporaryAccount> CreateTemporaryVirtualAccountAsync(TemporaryAccountRequest request,
        CngnRequestOptions? options = null, CancellationToken cancellationToken = default);
    Task<TransferResult> RedeemAssetAsync(RedeemRequest request,
        CngnRequestOptions? options = null, CancellationToken cancellationToken = default);
    Task<AccountVerification> VerifyBankAccountAsync(string bankCode, string accountNumber,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken cancellationToken = default);
    Task<JsonElement?> UpdateBankAccountAsync(BankAccountRequest request,
        CancellationToken cancellationToken = default);
    Task<TransferResult> WithdrawAsync(WithdrawRequest request,
        CngnRequestOptions? options = null, CancellationToken cancellationToken = default);
    Task<Transaction> VerifyWithdrawalAsync(string reference, CancellationToken cancellationToken = default);
    Task<BridgeQuote> GetBridgeQuoteAsync(BridgeQuoteRequest request,
        CancellationToken cancellationToken = default);
    Task<BridgeResult> BridgeAsync(BridgeRequest request,
        CngnRequestOptions? options = null, CancellationToken cancellationToken = default);
    Task<JsonElement?> WhitelistAddressAsync(string networkId, string address,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WhitelistEntry>> GetWhitelistedAddressesAsync(bool includeNetwork = false,
        CancellationToken cancellationToken = default);
}

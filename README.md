# cNGN .NET SDK

.NET 10 client for the cNGN API.

## Install

Add the `Cngn.Sdk` package to a .NET 10 project. The SDK uses Bouncy Castle and NBitcoin for wallet keys and libsodium for encrypted responses.

## Client

```csharp
using Cngn.Sdk;

using var client = new CngnClient(new CngnClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("CNGN_API_KEY")!,
    EncryptionKey = Environment.GetEnvironmentVariable("CNGN_ENCRYPTION_KEY")!,
    PrivateKey = File.ReadAllText("cngn-private-key.pem")
});

IReadOnlyList<Balance> balances = await client.GetBalanceAsync();
TransactionPage transactions = await client.GetTransactionsAsync(page: 1, limit: 20);
```

`PrivateKey` is the unencrypted Ed25519 OpenSSH private key registered with cNGN. Keep it and the encryption key out of source control. The environment is inferred from the API key prefix. A supplied `HttpClient` remains owned by the caller.

The client provides balances, transactions, networks, virtual accounts, bank verification, redemptions, withdrawals, bridge quotes and transfers, and address allowlisting. All calls accept a cancellation token. `IterateTransactionsAsync` fetches transaction pages as needed.

For operations that move funds, set an idempotency key when available:

```csharp
var result = await client.WithdrawAsync(
    new WithdrawRequest(100m, "0x...", "network-id"),
    new CngnRequestOptions { IdempotencyKey = "withdrawal-123" });
```

The client retries transient failures for read calls. Fund-moving calls are retried only when an idempotency key is supplied. It does not follow redirects with its own HTTP handler.

## Webhooks

Verify the signature against the exact request bytes before parsing the event:

```csharp
bool valid = Webhooks.Verify(rawBody, signatureHeader, webhookSecret);
if (valid)
{
    WebhookEvent webhook = Webhooks.Parse(rawBody);
}
```

## Wallets

`Wallets.Generate` creates a 24-word BIP-39 wallet. `Wallets.FromMnemonic` restores one. Supported networks are Ethereum, Base, BSC, Polygon, Tron, Solana, Stellar, and XBN. `Wallets.ValidateAddress` checks each network's address format.

Wallet results contain secrets. Store them securely and do not log them. Their `ToString()` output omits secrets.

## Development

Run `dotnet test Cngn.slnx` and `dotnet pack src/Cngn.Sdk/Cngn.Sdk.csproj`. Live checks require `CNGN_LIVE_TESTS=1`, `CNGN_API_KEY`, `CNGN_ENCRYPTION_KEY`, and `CNGN_PRIVATE_KEY_PATH`. They run only with a test API key.

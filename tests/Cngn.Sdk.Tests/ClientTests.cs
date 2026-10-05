using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cngn.Sdk.Tests;

public sealed class ClientTests
{
    [Fact]
    public async Task EncryptsRequestsAndSendsIdempotencyKey()
    {
        int calls = 0;
        using var http = new HttpClient(new Handler(async (request, cancellationToken) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.cngn.co/v1/api/withdraw", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("cngn_test_example", request.Headers.Authorization.Parameter);
            Assert.StartsWith("Cngn.Sdk/", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
            Assert.Equal("withdrawal-123", request.Headers.GetValues("Idempotency-Key").Single());

            byte[] wire = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            using JsonDocument envelope = JsonDocument.Parse(wire);
            Assert.Equal(2, envelope.RootElement.EnumerateObject().Count());
            byte[] iv = Convert.FromBase64String(envelope.RootElement.GetProperty("iv").GetString()!);
            byte[] cipher = Convert.FromBase64String(envelope.RootElement.GetProperty("content").GetString()!);
            using Aes aes = Aes.Create();
            aes.Key = SHA256.HashData("encryption-secret"u8);
            byte[] plaintext = aes.DecryptCbc(cipher, iv, PaddingMode.PKCS7);
            using JsonDocument body = JsonDocument.Parse(plaintext);
            Assert.Equal(100.25m, body.RootElement.GetProperty("amount").GetDecimal());
            Assert.Equal("0xabc", body.RootElement.GetProperty("address").GetString());
            Assert.Equal("network-1", body.RootElement.GetProperty("networkId").GetString());
            Assert.False(body.RootElement.GetProperty("shouldSaveAddress").GetBoolean());
            return Json(HttpStatusCode.OK, """{"status":200,"data":{"trxRef":"wd-123"}}""");
        }));
        using var client = new CngnClient(Options(), http);

        TransferResult result = await client.WithdrawAsync(
            new WithdrawRequest(100.25m, "0xabc", "network-1"),
            new CngnRequestOptions { IdempotencyKey = "withdrawal-123" },
            TestContext.Current.CancellationToken);

        Assert.Equal("wd-123", result.TrxRef);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DecryptsAuthenticatedResponse()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            $"{{\"status\":200,\"data\":\"{TestKey.BoxVector}\"}}"))));
        using var client = new CngnClient(Options(), http);

        JsonElement? result = await client.UpdateBankAccountAsync(
            new BankAccountRequest("Bank", "Ada", "1234567890"),
            TestContext.Current.CancellationToken);

        Assert.Equal("150000.00", result!.Value.GetProperty("balance").GetString());
        Assert.Equal("CNGN", result.Value.GetProperty("asset_code").GetString());
    }

    [Fact]
    public async Task RejectsTamperedEncryptedResponse()
    {
        byte[] payload = Convert.FromBase64String(TestKey.BoxVector);
        payload[30] ^= 1;
        string encoded = Convert.ToBase64String(payload);
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            $"{{\"status\":200,\"data\":\"{encoded}\"}}"))));
        using var client = new CngnClient(Options(), http);

        CngnException error = await Assert.ThrowsAsync<CngnException>(() =>
            client.UpdateBankAccountAsync(new BankAccountRequest("Bank", "Ada", "1234567890"),
                TestContext.Current.CancellationToken));

        Assert.Equal(CngnErrorKind.Decryption, error.Kind);
    }

    [Fact]
    public async Task DoesNotRetryUnkeyedTransfers()
    {
        int calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(Json(HttpStatusCode.ServiceUnavailable,
                """{"status":503,"message":"Service unavailable"}"""));
        }));
        using var client = new CngnClient(Options(), http);

        CngnException error = await Assert.ThrowsAsync<CngnException>(() =>
            client.WithdrawAsync(new WithdrawRequest(1m, "0xabc", "network-1"),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(CngnErrorKind.ServiceUnavailable, error.Kind);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RetriesKeyedTransfers()
    {
        int calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? Json(HttpStatusCode.ServiceUnavailable, """{"status":503,"message":"Service unavailable"}""")
                : Json(HttpStatusCode.OK, """{"status":200,"data":{"trxRef":"wd-123"}}"""));
        }));
        using var client = new CngnClient(Options(), http);

        TransferResult result = await client.WithdrawAsync(
            new WithdrawRequest(1m, "0xabc", "network-1"),
            new CngnRequestOptions { IdempotencyKey = "withdrawal-123" },
            TestContext.Current.CancellationToken);

        Assert.Equal("wd-123", result.TrxRef);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task BuildsReadQueriesAndMapsErrors()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("https://api.cngn.co/v1/api/transactions?page=2&limit=25",
                request.RequestUri!.ToString());
            return Task.FromResult(Json(HttpStatusCode.Unauthorized,
                """{"status":401,"message":"Invalid token"}"""));
        }));
        using var client = new CngnClient(Options() with { MaxRetries = 0 }, http);

        CngnException error = await Assert.ThrowsAsync<CngnException>(() =>
            client.GetTransactionsAsync(2, 25, TestContext.Current.CancellationToken));

        Assert.Equal(CngnErrorKind.Authentication, error.Kind);
        Assert.Equal(401, error.Status);
    }

    [Fact]
    public async Task ParsesTransactionPageAndDecimalStrings()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            """{"status":200,"data":{"data":[{"id":"tx-1","amount":"12.50","trx_ref":"ref-1","createdAt":"2026-01-02T03:04:05Z"}],"pagination":{"count":1,"pages":1,"isLastPage":true}}}"""))));
        using var client = new CngnClient(Options(), http);

        TransactionPage page = await client.GetTransactionsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Transaction transaction = Assert.Single(page.Data);
        Assert.Equal(12.50m, transaction.Amount);
        Assert.Equal("ref-1", transaction.Reference);
        Assert.Equal("2026-01-02T03:04:05Z", transaction.CreatedAt);
        Assert.True(page.Pagination.IsLastPage);
    }

    [Fact]
    public async Task MapsNonJsonForbiddenResponse()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.Forbidden)
        {
            Content = new StringContent("<html>Forbidden</html>", Encoding.UTF8, "text/html")
        })));
        using var client = new CngnClient(Options() with { MaxRetries = 0 }, http);

        CngnException error = await Assert.ThrowsAsync<CngnException>(() =>
            client.GetBalanceAsync(TestContext.Current.CancellationToken));

        Assert.Equal(CngnErrorKind.Permission, error.Kind);
        Assert.Equal(403, error.Status);
    }

    [Fact]
    public void RejectsUnsafeConfiguration()
    {
        Assert.Equal(CngnErrorKind.Configuration,
            Assert.Throws<CngnException>(() => new CngnClient(Options() with
            { BaseUri = new Uri("http://example.com/v1/api/") })).Kind);
        Assert.Equal(CngnErrorKind.Configuration,
            Assert.Throws<CngnException>(() => new CngnClient(Options() with
            { Environment = CngnEnvironment.Live })).Kind);
    }

    [Fact]
    public void RejectsDotSegmentWithdrawalReferences()
    {
        using var client = new CngnClient(Options());
        Assert.Throws<ArgumentException>(() =>
        {
            _ = client.VerifyWithdrawalAsync(".", TestContext.Current.CancellationToken);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            _ = client.VerifyWithdrawalAsync("..", TestContext.Current.CancellationToken);
        });
    }

    private static CngnClientOptions Options() => new()
    {
        ApiKey = "cngn_test_example",
        EncryptionKey = "encryption-secret",
        PrivateKey = TestKey.Pem(),
        MaxRetries = 1
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}

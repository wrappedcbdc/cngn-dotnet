using System.Security.Cryptography;
using System.Text;

namespace Cngn.Sdk.Tests;

public sealed class WebhookTests
{
    [Fact]
    public void VerifiesRawBodyAndParsesEvent()
    {
        byte[] body = Encoding.UTF8.GetBytes("{\"event\":\"withdrawal.completed\",\"data\":{\"reference\":\"wd-1\"}}");
        string signature = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("secret"), body));
        Assert.True(Webhooks.Verify(body, signature, "secret"));
        Assert.False(Webhooks.Verify(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(body) + " "), signature, "secret"));
        Assert.False(Webhooks.Verify(body, "sha256=bad", "secret"));
        Assert.Equal("wd-1", Webhooks.Parse(body).Data.GetProperty("reference").GetString());
    }

    [Fact]
    public void RejectsInvalidPayload()
    {
        Assert.Equal(CngnErrorKind.Validation,
            Assert.Throws<CngnException>(() => Webhooks.Parse("{}"u8)).Kind);
    }
}

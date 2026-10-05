using System.Buffers.Binary;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;

namespace Cngn.Sdk.Tests;

internal static class TestKey
{
    public const string BoxVector = "AwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDnjFK7eDgrvJLL/GOccUK+glTymjxdmp8CfFpye8Bh2bj/F514xMD1oSKdc+HWqsqIr3RfOLUlJYDPZ3zt9DOjTrRzLYz7HtwwXgUpcduzQKWhQUNNEdFugWHDlh9WQ==";

    public static string Pem()
    {
        byte[] seed = Enumerable.Repeat((byte)1, 32).ToArray();
        byte[] publicKey = new Ed25519PrivateKeyParameters(seed, 0).GeneratePublicKey().GetEncoded();
        byte[] publicBlock = Join(SshString("ssh-ed25519"), SshString(publicKey));
        byte[] secret = Join(seed, publicKey);
        byte[] privateBlock = Join(UInt32(42), UInt32(42), SshString("ssh-ed25519"),
            SshString(publicKey), SshString(secret), SshString([]));
        int paddingLength = 8 - privateBlock.Length % 8;
        byte[] padding = Enumerable.Range(1, paddingLength).Select(value => (byte)value).ToArray();
        byte[] blob = Join(Encoding.ASCII.GetBytes("openssh-key-v1\0"), SshString("none"),
            SshString("none"), SshString([]), UInt32(1), SshString(publicBlock),
            SshString(Join(privateBlock, padding)));
        return $"-----BEGIN OPENSSH PRIVATE KEY-----\n{Convert.ToBase64String(blob)}\n-----END OPENSSH PRIVATE KEY-----\n";
    }

    private static byte[] UInt32(uint value)
    {
        byte[] result = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(result, value);
        return result;
    }

    private static byte[] SshString(string value) => SshString(Encoding.ASCII.GetBytes(value));

    private static byte[] SshString(byte[] value) => Join(UInt32((uint)value.Length), value);

    private static byte[] Join(params byte[][] values)
    {
        using var stream = new MemoryStream();
        foreach (byte[] value in values) stream.Write(value);
        return stream.ToArray();
    }
}

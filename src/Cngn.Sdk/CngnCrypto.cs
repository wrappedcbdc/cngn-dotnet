using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cngn.Sdk;

internal sealed class CngnCrypto : IDisposable
{
    private readonly byte[] _aesKey;
    private readonly byte[] _curveSecret;
    private bool _disposed;

    public CngnCrypto(string encryptionKey, string privateKey)
    {
        if (string.IsNullOrWhiteSpace(encryptionKey))
            throw new CngnException(CngnErrorKind.Configuration, "Encryption key is required");

        byte[] seed = ParseOpenSshSeed(privateKey);
        try
        {
            _aesKey = SHA256.HashData(Encoding.UTF8.GetBytes(encryptionKey));
            (byte[] _, byte[] privateKeyBytes) = SodiumNative.Ed25519KeyPair(seed);
            try
            {
                _curveSecret = SodiumNative.Curve25519Secret(privateKeyBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateKeyBytes);
            }
        }
        catch (Exception error) when (error is CryptographicException or ArgumentException)
        {
            CryptographicOperations.ZeroMemory(_aesKey);
            throw new CngnException(CngnErrorKind.Configuration, "Invalid Ed25519 private key", innerException: error);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(seed);
        }
    }

    public EncryptedBody Encrypt(object value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(value, CngnJson.Options);
        try
        {
            byte[] iv = RandomNumberGenerator.GetBytes(16);
            using Aes aes = Aes.Create();
            aes.Key = _aesKey;
            byte[] ciphertext = aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7);
            return new EncryptedBody(Convert.ToBase64String(ciphertext), Convert.ToBase64String(iv));
        }
        catch (CryptographicException error)
        {
            throw new CngnException(CngnErrorKind.Encryption, "Could not encrypt the request", innerException: error);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public JsonElement DecryptResponse(string encoded)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(encoded);
        }
        catch (FormatException error)
        {
            throw new CngnException(CngnErrorKind.Decryption, "Invalid encrypted response", innerException: error);
        }

        if (blob.Length < 72)
            throw new CngnException(CngnErrorKind.Decryption, "Encrypted response is too short");

        byte[] nonce = blob.AsSpan(0, 24).ToArray();
        byte[] ciphertext = blob.AsSpan(24, blob.Length - 56).ToArray();
        byte[] publicKey = blob.AsSpan(blob.Length - 32).ToArray();
        byte[] plaintext;
        try
        {
            plaintext = SodiumNative.OpenBox(ciphertext, nonce, publicKey, _curveSecret);
        }
        catch (CryptographicException error)
        {
            throw new CngnException(CngnErrorKind.Decryption,
                "Could not authenticate or decrypt the response", innerException: error);
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(plaintext);
            JsonElement root = document.RootElement;
            if (root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                throw new CngnException(CngnErrorKind.Decryption, "Invalid decrypted response shape");
            return root.Clone();
        }
        catch (JsonException error)
        {
            throw new CngnException(CngnErrorKind.Decryption, "Invalid decrypted response JSON", innerException: error);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_aesKey);
        CryptographicOperations.ZeroMemory(_curveSecret);
    }

    private static byte[] ParseOpenSshSeed(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
            throw new CngnException(CngnErrorKind.Configuration, "OpenSSH private key is required");

        const string header = "-----BEGIN OPENSSH PRIVATE KEY-----";
        const string footer = "-----END OPENSSH PRIVATE KEY-----";
        string[] lines = pem.Trim().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 3 || lines[0] != header || lines[^1] != footer)
            throw new CngnException(CngnErrorKind.Configuration, "Expected an OpenSSH Ed25519 private key");

        byte[]? blob = null;
        try
        {
            blob = Convert.FromBase64String(string.Concat(lines[1..^1]));
            var reader = new SshReader(blob);
            if (!reader.ReadBytes(15).SequenceEqual("openssh-key-v1\0"u8)
                || !reader.ReadString().SequenceEqual("none"u8)
                || !reader.ReadString().SequenceEqual("none"u8)
                || reader.ReadString().Length != 0
                || reader.ReadUInt32() != 1)
                throw new FormatException("Unsupported OpenSSH key format");

            var publicReader = new SshReader(reader.ReadString());
            if (!publicReader.ReadString().SequenceEqual("ssh-ed25519"u8))
                throw new FormatException("Expected an Ed25519 key");
            ReadOnlySpan<byte> publicKey = publicReader.ReadString();
            if (publicKey.Length != 32 || publicReader.Remaining != 0)
                throw new FormatException("Invalid public key");

            var privateReader = new SshReader(reader.ReadString());
            if (reader.Remaining != 0 || privateReader.ReadUInt32() != privateReader.ReadUInt32()
                || !privateReader.ReadString().SequenceEqual("ssh-ed25519"u8))
                throw new FormatException("Invalid private key block");
            ReadOnlySpan<byte> innerPublic = privateReader.ReadString();
            ReadOnlySpan<byte> secret = privateReader.ReadString();
            if (innerPublic.Length != 32 || secret.Length != 64
                || !CryptographicOperations.FixedTimeEquals(innerPublic, publicKey)
                || !CryptographicOperations.FixedTimeEquals(secret[32..], publicKey))
                throw new FormatException("Public and private keys disagree");

            privateReader.ReadString();
            for (byte expected = 1; privateReader.Remaining > 0; expected++)
            {
                if (privateReader.ReadBytes(1)[0] != expected)
                    throw new FormatException("Invalid OpenSSH padding");
            }

            byte[] seed = secret[..32].ToArray();
            (byte[] generatedPublic, byte[] generatedPrivate) = SodiumNative.Ed25519KeyPair(seed);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(generatedPublic, publicKey))
                {
                    CryptographicOperations.ZeroMemory(seed);
                    throw new FormatException("Private key does not match its public key");
                }
                return seed;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(generatedPrivate);
            }
        }
        catch (Exception error) when (error is FormatException or CryptographicException or ArgumentException)
        {
            throw new CngnException(CngnErrorKind.Configuration, "Invalid OpenSSH Ed25519 private key", innerException: error);
        }
        finally
        {
            if (blob is not null) CryptographicOperations.ZeroMemory(blob);
        }
    }

    private ref struct SshReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _offset;

        public int Remaining => _data.Length - _offset;

        public ReadOnlySpan<byte> ReadBytes(int length)
        {
            if (length < 0 || length > Remaining) throw new FormatException("Truncated OpenSSH key");
            ReadOnlySpan<byte> value = _data.Slice(_offset, length);
            _offset += length;
            return value;
        }

        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4));

        public ReadOnlySpan<byte> ReadString()
        {
            uint length = ReadUInt32();
            if (length > int.MaxValue) throw new FormatException("Invalid OpenSSH string length");
            return ReadBytes((int)length);
        }
    }
}

internal sealed record EncryptedBody(string Content, string Iv);

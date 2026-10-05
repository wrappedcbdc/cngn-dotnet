using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NBitcoin;
using NBitcoin.DataEncoders;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;

namespace Cngn.Sdk;

public enum WalletNetwork
{
    Ethereum,
    Base,
    Bsc,
    Polygon,
    Tron,
    Solana,
    Stellar,
    Xbn
}

public sealed class GeneratedWallet
{
    public WalletNetwork Network { get; }
    public string Address { get; }
    public string PrivateKey { get; }
    public string Mnemonic { get; }

    internal GeneratedWallet(WalletNetwork network, string address, string privateKey, string mnemonic)
    {
        Network = network;
        Address = address;
        PrivateKey = privateKey;
        Mnemonic = mnemonic;
    }

    public override string ToString() => $"GeneratedWallet {{ Network = {Network}, Address = {Address}, Secrets = [redacted] }}";
}

public static class Wallets
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private static readonly WalletNetwork[] EvmNetworks =
        [WalletNetwork.Ethereum, WalletNetwork.Base, WalletNetwork.Bsc, WalletNetwork.Polygon];

    public static GeneratedWallet Generate(WalletNetwork network)
    {
        byte[] entropy = RandomNumberGenerator.GetBytes(32);
        try
        {
            string mnemonic = new Mnemonic(Wordlist.English, entropy).ToString();
            return FromMnemonic(network, mnemonic);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    public static GeneratedWallet FromMnemonic(WalletNetwork network, string mnemonic)
    {
        if (!Enum.IsDefined(network)) throw new ArgumentOutOfRangeException(nameof(network));
        if (string.IsNullOrWhiteSpace(mnemonic)) throw new ArgumentException("Mnemonic is required", nameof(mnemonic));

        Mnemonic phrase;
        try
        {
            phrase = new Mnemonic(mnemonic, Wordlist.English);
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        {
            throw new ArgumentException("Invalid BIP-39 mnemonic", nameof(mnemonic), error);
        }

        byte[] masterSeed = phrase.DeriveSeed();
        try
        {
            if (EvmNetworks.Contains(network) || network == WalletNetwork.Tron)
                return DeriveSecp256k1(network, mnemonic, masterSeed);

            int[] path = network switch
            {
                WalletNetwork.Solana => [44, 501, 0, 0],
                WalletNetwork.Stellar => [44, 148, 0],
                WalletNetwork.Xbn => [44, 703, 0],
                _ => throw new ArgumentOutOfRangeException(nameof(network))
            };
            byte[] seed = DeriveEd25519Seed(masterSeed, path);
            try
            {
                byte[] publicKey = new Ed25519PrivateKeyParameters(seed, 0).GeneratePublicKey().GetEncoded();
                if (network == WalletNetwork.Solana)
                {
                    byte[] secret = new byte[64];
                    seed.CopyTo(secret, 0);
                    publicKey.CopyTo(secret, 32);
                    try
                    {
                        return new GeneratedWallet(network, Encoders.Base58.EncodeData(publicKey),
                            Encoders.Base58.EncodeData(secret), mnemonic);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(secret);
                    }
                }
                return new GeneratedWallet(network, EncodeStrKey(0x30, publicKey),
                    EncodeStrKey(0x90, seed), mnemonic);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(seed);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterSeed);
        }
    }

    public static bool ValidateAddress(WalletNetwork network, string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !Enum.IsDefined(network)) return false;
        try
        {
            if (EvmNetworks.Contains(network)) return IsEvmAddress(address);
            if (network == WalletNetwork.Tron)
            {
                byte[] payload = Encoders.Base58Check.DecodeData(address);
                return payload.Length == 21 && payload[0] == 0x41;
            }
            if (network == WalletNetwork.Solana)
                return Encoders.Base58.DecodeData(address).Length == 32;
            return IsStrKey(address, 0x30);
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static GeneratedWallet DeriveSecp256k1(WalletNetwork network,
        string mnemonic, byte[] masterSeed)
    {
        string path = network == WalletNetwork.Tron ? "44'/195'/0'/0/0" : "44'/60'/0'/0/0";
        Key key = ExtKey.CreateFromSeed(masterSeed).Derive(new KeyPath(path)).PrivateKey;
        byte[] privateKey = key.ToBytes();
        try
        {
            byte[] publicKey = key.PubKey.Decompress().ToBytes();
            byte[] hash = Keccak(publicKey.AsSpan(1));
            string address = network == WalletNetwork.Tron
                ? TronAddress(hash.AsSpan(12))
                : EvmAddress(hash.AsSpan(12));
            return new GeneratedWallet(network, address,
                Convert.ToHexStringLower(privateKey), mnemonic);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private static string EvmAddress(ReadOnlySpan<byte> addressBytes)
    {
        string lower = Convert.ToHexStringLower(addressBytes);
        byte[] checksum = Keccak(Encoding.ASCII.GetBytes(lower));
        char[] result = lower.ToCharArray();
        for (int index = 0; index < result.Length; index++)
        {
            int nibble = index % 2 == 0 ? checksum[index / 2] >> 4 : checksum[index / 2] & 15;
            if (nibble >= 8) result[index] = char.ToUpperInvariant(result[index]);
        }
        return "0x" + new string(result);
    }

    private static bool IsEvmAddress(string address)
    {
        if (address.Length != 42 || !address.StartsWith("0x", StringComparison.Ordinal)) return false;
        string body = address[2..];
        if (!body.All(Uri.IsHexDigit)) return false;
        if (body == body.ToLowerInvariant() || body == body.ToUpperInvariant()) return true;
        byte[] raw = Convert.FromHexString(body);
        return EvmAddress(raw) == address;
    }

    private static string TronAddress(ReadOnlySpan<byte> addressBytes)
    {
        byte[] payload = new byte[21];
        payload[0] = 0x41;
        addressBytes.CopyTo(payload.AsSpan(1));
        return Encoders.Base58Check.EncodeData(payload);
    }

    private static byte[] Keccak(ReadOnlySpan<byte> data)
    {
        var digest = new KeccakDigest(256);
        byte[] input = data.ToArray();
        digest.BlockUpdate(input, 0, input.Length);
        byte[] output = new byte[32];
        digest.DoFinal(output, 0);
        return output;
    }

    private static byte[] DeriveEd25519Seed(byte[] masterSeed, IReadOnlyList<int> path)
    {
        byte[] node = HMACSHA512.HashData("ed25519 seed"u8, masterSeed);
        foreach (int index in path)
        {
            byte[] data = new byte[37];
            node.AsSpan(0, 32).CopyTo(data.AsSpan(1));
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(33), (uint)index | 0x80000000);
            byte[] next = HMACSHA512.HashData(node.AsSpan(32), data);
            CryptographicOperations.ZeroMemory(node);
            CryptographicOperations.ZeroMemory(data);
            node = next;
        }
        byte[] result = node.AsSpan(0, 32).ToArray();
        CryptographicOperations.ZeroMemory(node);
        return result;
    }

    private static string EncodeStrKey(byte version, ReadOnlySpan<byte> key)
    {
        byte[] payload = new byte[35];
        payload[0] = version;
        key.CopyTo(payload.AsSpan(1, 32));
        ushort checksum = Crc16(payload.AsSpan(0, 33));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(33), checksum);
        var result = new StringBuilder(56);
        int pending = 0;
        int bits = 0;
        foreach (byte value in payload)
        {
            pending = (pending << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                result.Append(Base32Alphabet[(pending >> bits) & 31]);
            }
        }
        if (bits > 0) result.Append(Base32Alphabet[(pending << (5 - bits)) & 31]);
        return result.ToString();
    }

    private static bool IsStrKey(string address, byte version)
    {
        if (address.Length != 56) return false;
        byte[] bytes = new byte[35];
        int pending = 0;
        int bits = 0;
        int offset = 0;
        foreach (char character in address)
        {
            int digit = Base32Alphabet.IndexOf(character);
            if (digit < 0) return false;
            pending = (pending << 5) | digit;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                if (offset >= bytes.Length) return false;
                bytes[offset++] = (byte)(pending >> bits);
            }
        }
        if (offset != 35 || (pending & ((1 << bits) - 1)) != 0 || bytes[0] != version) return false;
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(33)) == Crc16(bytes.AsSpan(0, 33));
    }

    private static ushort Crc16(ReadOnlySpan<byte> value)
    {
        ushort crc = 0;
        foreach (byte item in value)
        {
            crc ^= (ushort)(item << 8);
            for (int bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }
}

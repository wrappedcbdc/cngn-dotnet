using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Cngn.Sdk;

internal static unsafe partial class SodiumNative
{
    static SodiumNative()
    {
        if (SodiumInit() < 0)
            throw new CryptographicException("Could not initialize libsodium");
    }

    public static (byte[] PublicKey, byte[] PrivateKey) Ed25519KeyPair(ReadOnlySpan<byte> seed)
    {
        if (seed.Length != 32) throw new ArgumentException("Expected a 32 byte seed", nameof(seed));
        byte[] publicKey = new byte[32];
        byte[] privateKey = new byte[64];
        fixed (byte* publicPointer = publicKey, privatePointer = privateKey, seedPointer = seed)
        {
            if (CryptoSignSeedKeyPair(publicPointer, privatePointer, seedPointer) != 0)
                throw new CryptographicException("Could not derive Ed25519 key pair");
        }
        return (publicKey, privateKey);
    }

    public static byte[] Curve25519Secret(ReadOnlySpan<byte> ed25519PrivateKey)
    {
        if (ed25519PrivateKey.Length != 64)
            throw new ArgumentException("Expected a 64 byte Ed25519 private key", nameof(ed25519PrivateKey));
        byte[] curveSecret = new byte[32];
        fixed (byte* curvePointer = curveSecret, edPointer = ed25519PrivateKey)
        {
            if (CryptoSignEd25519SecretToCurve25519(curvePointer, edPointer) != 0)
                throw new CryptographicException("Could not convert Ed25519 key");
        }
        return curveSecret;
    }

    public static byte[] OpenBox(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> secretKey)
    {
        if (ciphertext.Length < 16 || nonce.Length != 24 || publicKey.Length != 32 || secretKey.Length != 32)
            throw new CryptographicException("Invalid encrypted response");
        byte[] plaintext = new byte[ciphertext.Length - 16];
        fixed (byte* plainPointer = plaintext, cipherPointer = ciphertext,
            noncePointer = nonce, publicPointer = publicKey, secretPointer = secretKey)
        {
            if (CryptoBoxOpenEasy(plainPointer, cipherPointer, (ulong)ciphertext.Length,
                noncePointer, publicPointer, secretPointer) != 0)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                throw new CryptographicException("Response authentication failed");
            }
        }
        return plaintext;
    }

    [LibraryImport("libsodium", EntryPoint = "sodium_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int SodiumInit();

    [LibraryImport("libsodium", EntryPoint = "crypto_sign_seed_keypair")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int CryptoSignSeedKeyPair(byte* publicKey, byte* privateKey, byte* seed);

    [LibraryImport("libsodium", EntryPoint = "crypto_sign_ed25519_sk_to_curve25519")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int CryptoSignEd25519SecretToCurve25519(byte* curveSecret, byte* ed25519PrivateKey);

    [LibraryImport("libsodium", EntryPoint = "crypto_box_open_easy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int CryptoBoxOpenEasy(byte* plaintext, byte* ciphertext, ulong ciphertextLength,
        byte* nonce, byte* publicKey, byte* secretKey);
}

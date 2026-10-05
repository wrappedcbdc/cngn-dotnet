namespace Cngn.Sdk.Tests;

public sealed class WalletTests
{
    private const string Phrase = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    [Theory]
    [InlineData(WalletNetwork.Ethereum, "0x9858EfFD232B4033E47d90003D41EC34EcaEda94")]
    [InlineData(WalletNetwork.Base, "0x9858EfFD232B4033E47d90003D41EC34EcaEda94")]
    [InlineData(WalletNetwork.Tron, "TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH")]
    [InlineData(WalletNetwork.Solana, "HAgk14JpMQLgt6rVgv7cBQFJWFto5Dqxi472uT3DKpqk")]
    [InlineData(WalletNetwork.Stellar, "GB3JDWCQJCWMJ3IILWIGDTQJJC5567PGVEVXSCVPEQOTDN64VJBDQBYX")]
    [InlineData(WalletNetwork.Xbn, "GD7OOH7FGE25NJDQ67X23AB5IG6UGGOFV6HKOB6AKWTXFAXR262ZEAVB")]
    public void DerivesKnownWallets(WalletNetwork network, string expected)
    {
        GeneratedWallet wallet = Wallets.FromMnemonic(network, Phrase);
        Assert.Equal(expected, wallet.Address);
        Assert.True(Wallets.ValidateAddress(network, wallet.Address));
        Assert.DoesNotContain(Phrase, wallet.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(wallet.PrivateKey, wallet.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidAddressesAndMnemonics()
    {
        Assert.Throws<ArgumentException>(() => Wallets.FromMnemonic(WalletNetwork.Ethereum, "invalid"));
        Assert.False(Wallets.ValidateAddress(WalletNetwork.Ethereum, "0x123"));
        Assert.False(Wallets.ValidateAddress(WalletNetwork.Tron, "TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH!"));
        Assert.False(Wallets.ValidateAddress(WalletNetwork.Solana, "bad"));
        Assert.False(Wallets.ValidateAddress(WalletNetwork.Stellar, "bad"));
    }

    [Fact]
    public void GeneratesWallet()
    {
        GeneratedWallet wallet = Wallets.Generate(WalletNetwork.Ethereum);
        Assert.True(Wallets.ValidateAddress(WalletNetwork.Ethereum, wallet.Address));
        Assert.Equal(24, wallet.Mnemonic.Split(' ').Length);
    }
}

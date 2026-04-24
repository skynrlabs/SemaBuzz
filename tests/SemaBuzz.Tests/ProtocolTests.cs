using System.Security.Cryptography;
using SemaBuzz.Protocol;
using Xunit;

namespace SemaBuzz.Tests;

// ─────────────────────────────────────────────────────────────
// SemaBuzzPacket  — serialise/deserialise round-trip
// ─────────────────────────────────────────────────────────────

public class SemaBuzzPacketTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new SemaBuzzPacket('Z', 200, SemaBuzzPacketType.Char, seqNum: 42);
        var bytes = original.ToWireBytes();
        var decoded = SemaBuzzPacket.FromWireBytes(bytes);

        Assert.NotNull(decoded);
        Assert.Equal('Z', decoded!.Value.Character);
        Assert.Equal(200, decoded!.Value.Intensity);
        Assert.Equal(SemaBuzzPacketType.Char, decoded!.Value.Type);
        Assert.Equal((ushort)42, decoded!.Value.SeqNum);
    }

    [Fact]
    public void WireSize_Is8Bytes()
    {
        var pkt = new SemaBuzzPacket('A', 128);
        var bytes = pkt.ToWireBytes();
        Assert.Equal(SemaBuzzPacket.WireSize, bytes.Length);
    }

    [Fact]
    public void FromWireBytes_ReturnsNull_ForBadMagic()
    {
        var bytes = new SemaBuzzPacket('A', 0).ToWireBytes();
        bytes[0] = 0xFF; // corrupt magic
        Assert.Null(SemaBuzzPacket.FromWireBytes(bytes));
    }

    [Fact]
    public void FromWireBytes_ReturnsNull_ForWrongLength()
    {
        Assert.Null(SemaBuzzPacket.FromWireBytes(new byte[5]));
    }

    [Theory]
    [InlineData('€')]
    [InlineData('\n')]
    [InlineData(' ')]
    public void RoundTrip_UnicodeCharacters(char ch)
    {
        var pkt = new SemaBuzzPacket(ch, 0);
        var decoded = SemaBuzzPacket.FromWireBytes(pkt.ToWireBytes());
        Assert.Equal(ch, decoded!.Value.Character);
    }
}

// ─────────────────────────────────────────────────────────────
// SemaBuzzKeyExchange  — serialise/deserialise round-trip
// ─────────────────────────────────────────────────────────────

public class SemaBuzzKeyExchangeTests
{
    [Fact]
    public void RoundTrip_RealEcdhPublicKey()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var keyBytes = ecdh.PublicKey.ExportSubjectPublicKeyInfo();
        var wire = SemaBuzzKeyExchange.Serialize(keyBytes);
        var decoded = SemaBuzzKeyExchange.Deserialize(wire);

        Assert.NotNull(decoded);
        Assert.Equal(keyBytes, decoded);
    }

    [Fact]
    public void IsKeyExchangePacket_ReturnsFalse_ForShortBuffer()
    {
        Assert.False(SemaBuzzKeyExchange.IsKeyExchangePacket(new byte[3]));
    }

    [Fact]
    public void Deserialize_ReturnsNull_ForGarbageData()
    {
        Assert.Null(SemaBuzzKeyExchange.Deserialize(new byte[] { 0x00, 0x01, 0x02, 0x03 }));
    }
}

// ─────────────────────────────────────────────────────────────
// SemaBuzzShield  — AES-256-GCM encrypt/decrypt
// ─────────────────────────────────────────────────────────────

public class SemaBuzzShieldTests
{
    private static byte[] RandomKey() { var k = new byte[32]; RandomNumberGenerator.Fill(k); return k; }

    [Fact]
    public void EncryptDecrypt_RoundTrip()
    {
        using var shield = new SemaBuzzShield(RandomKey());
        var plaintext = new SemaBuzzPacket('H', 99).ToWireBytes();
        var ciphertext = shield.Encrypt(plaintext);
        var recovered = shield.Decrypt(ciphertext);

        Assert.NotNull(recovered);
        Assert.Equal(plaintext, recovered);
    }

    [Fact]
    public void Decrypt_ReturnsNull_WhenTagTampered()
    {
        using var shield = new SemaBuzzShield(RandomKey());
        var ct = shield.Encrypt(new byte[] { 1, 2, 3 });
        ct[14] ^= 0xFF; // flip bits in the tag
        Assert.Null(shield.Decrypt(ct));
    }

    [Fact]
    public void TwoShields_SameKey_CrossDecrypt()
    {
        var key = RandomKey();
        using var a = new SemaBuzzShield((byte[])key.Clone());
        using var b = new SemaBuzzShield(key);
        var plain = new byte[] { 42, 43, 44 };
        Assert.Equal(plain, b.Decrypt(a.Encrypt(plain)));
    }

    [Fact]
    public void FromEcdhSecret_ProducesWorkingShield()
    {
        using var alice = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var bob = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        var aliceSecret = alice.DeriveRawSecretAgreement(bob.PublicKey);
        var bobSecret = bob.DeriveRawSecretAgreement(alice.PublicKey);

        // HKDF must produce the same key from the same raw secret
        using var shieldA = SemaBuzzShield.FromEcdhSecret(aliceSecret);
        using var shieldB = SemaBuzzShield.FromEcdhSecret(bobSecret);

        var plain = new SemaBuzzPacket('X', 0).ToWireBytes();
        var ct = shieldA.Encrypt(plain);
        Assert.Equal(plain, shieldB.Decrypt(ct));
    }
}

// ─────────────────────────────────────────────────────────────
// SemaBuzzRelayPacket  — build / parse / token generation
// ─────────────────────────────────────────────────────────────

public class SemaBuzzRelayPacketTests
{
    [Fact]
    public void BuildAndParse_RoundTrip()
    {
        var token = SemaBuzzRelayPacket.GenerateToken();
        var wire = SemaBuzzRelayPacket.Build(SemaBuzzRelayPacketType.JoinHost, token);
        var parsed = SemaBuzzRelayPacket.Parse(wire);

        Assert.NotNull(parsed);
        Assert.Equal(SemaBuzzRelayPacketType.JoinHost, parsed!.Value.Type);
        Assert.Equal(token, parsed!.Value.Token);
    }

    [Fact]
    public void IsRelayPacket_ReturnsFalse_ForSemaBuzzPacket()
    {
        var sbPacket = new SemaBuzzPacket('A', 0).ToWireBytes();
        Assert.False(SemaBuzzRelayPacket.IsRelayPacket(sbPacket));
    }

    [Fact]
    public void GenerateToken_IsAlwaysSixChars()
    {
        for (int i = 0; i < 200; i++)
            Assert.Equal(SemaBuzzRelayPacket.TokenLength, SemaBuzzRelayPacket.GenerateToken().Length);
    }

    [Fact]
    public void GenerateToken_ContainsNoAmbiguousChars()
    {
        const string forbidden = "IO01";
        for (int i = 0; i < 500; i++)
            Assert.DoesNotContain(SemaBuzzRelayPacket.GenerateToken(), c => forbidden.Contains(c));
    }

    [Fact]
    public void WireSize_Is10Bytes()
    {
        var wire = SemaBuzzRelayPacket.Build(SemaBuzzRelayPacketType.JoinDial, "ABCDEF");
        Assert.Equal(SemaBuzzRelayPacket.Size, wire.Length);
    }
}

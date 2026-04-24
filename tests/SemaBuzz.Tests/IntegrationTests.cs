using System.Security.Cryptography;
using SemaBuzz.Protocol;
using Xunit;

namespace SemaBuzz.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Integration tests — multiple Protocol components working together.
// Tagged [Trait("Category","Integration")] so CI can run them separately.
// ─────────────────────────────────────────────────────────────────────────────

public class FullSessionTests
{
    /// <summary>
    /// Full ECDH key-exchange → derive shield on both sides → exchange 100 packets
    /// each way.  Exercises SemaBuzzKeyExchange + SemaBuzzShield nonce counter
    /// management together.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void EcdhHandshake_ThenMultiPacketExchange_AllDecryptCorrectly()
    {
        // ── Key exchange ──────────────────────────────────────────────────────
        using var alice = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var bob   = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        var aliceWire = SemaBuzzKeyExchange.Serialize(
            alice.PublicKey.ExportSubjectPublicKeyInfo());
        var bobWire   = SemaBuzzKeyExchange.Serialize(
            bob.PublicKey.ExportSubjectPublicKeyInfo());

        var alicePubBytes = SemaBuzzKeyExchange.Deserialize(aliceWire)!;
        var bobPubBytes   = SemaBuzzKeyExchange.Deserialize(bobWire)!;

        using var alicePub = ECDiffieHellman.Create();
        alicePub.ImportSubjectPublicKeyInfo(alicePubBytes, out _);

        using var bobPub = ECDiffieHellman.Create();
        bobPub.ImportSubjectPublicKeyInfo(bobPubBytes, out _);

        // ── Derive session shields ────────────────────────────────────────────
        using var shieldAlice = SemaBuzzShield.FromEcdhSecret(
            alice.DeriveRawSecretAgreement(bobPub.PublicKey));
        using var shieldBob   = SemaBuzzShield.FromEcdhSecret(
            bob.DeriveRawSecretAgreement(alicePub.PublicKey));

        // ── Exchange 100 packets Alice → Bob ──────────────────────────────────
        for (int i = 0; i < 100; i++)
        {
            var plain  = new SemaBuzzPacket((char)('A' + (i % 26)), (byte)(i % 256),
                                            SemaBuzzPacketType.Char, (ushort)i).ToWireBytes();
            var ct        = shieldAlice.Encrypt(plain);
            var recovered = shieldBob.Decrypt(ct);

            Assert.NotNull(recovered);
            Assert.Equal(plain, recovered);
        }

        // ── Exchange 100 packets Bob → Alice ──────────────────────────────────
        for (int i = 0; i < 100; i++)
        {
            var plain  = new SemaBuzzPacket((char)('a' + (i % 26)), (byte)(i % 256),
                                            SemaBuzzPacketType.Char, (ushort)i).ToWireBytes();
            var ct        = shieldBob.Encrypt(plain);
            var recovered = shieldAlice.Decrypt(ct);

            Assert.NotNull(recovered);
            Assert.Equal(plain, recovered);
        }
    }

    /// <summary>
    /// Metadata pipeline: build a realistic metadata object with handle and avatar,
    /// serialize through the wire format, deserialize, and verify all fields survive.
    /// Exercises SemaBuzzMetadata working alongside SemaBuzzShield encryption.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void MetadataPipeline_WithEncryption_PreservesAllFields()
    {
        var avatar = new byte[128];
        RandomNumberGenerator.Fill(avatar);

        var wire = SemaBuzzMetadata.Serialize("TestUser", avatar);

        // encrypt the metadata wire bytes as they would be sent over the relay
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        using var shield = new SemaBuzzShield(key);

        var ciphertext    = shield.Encrypt(wire);
        var decryptedWire = shield.Decrypt(ciphertext);

        Assert.NotNull(decryptedWire);

        var decoded = SemaBuzzMetadata.Deserialize(decryptedWire!);

        Assert.NotNull(decoded);
        Assert.Equal("TestUser", decoded!.Value.Handle);
        Assert.Equal(avatar, decoded.Value.AvatarPng);
    }

    /// <summary>
    /// Passphrase-derived shield: PBKDF2 key derivation → encrypt 50 packets →
    /// decrypt with a second shield derived from the same passphrase + salt.
    /// Exercises the full passphrase path end-to-end.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void PassphraseShield_SamePassphrase_CrossDecryptsAllPackets()
    {
        const string passphrase = "correct-horse-battery-staple";

        using var shieldA = SemaBuzzShield.FromPassphrase(passphrase);
        using var shieldB = SemaBuzzShield.FromPassphrase(passphrase);

        for (int i = 0; i < 50; i++)
        {
            var plain     = new SemaBuzzPacket((char)('A' + (i % 26)), 128).ToWireBytes();
            var ct        = shieldA.Encrypt(plain);
            var recovered = shieldB.Decrypt(ct);

            Assert.NotNull(recovered);
            Assert.Equal(plain, recovered);
        }
    }

    /// <summary>
    /// Streamer pipeline: simulate keystrokes at varying intervals and verify that
    /// the produced packets have monotonically increasing sequence numbers,
    /// intensities within the valid 0–255 range, and the correct packet type.
    /// Exercises SemaBuzzStreamer → SemaBuzzPacket together.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void Streamer_SimulatedKeystrokes_ProducesValidPacketSequence()
    {
        var streamer = new SemaBuzzStreamer();
        var packets  = new List<SemaBuzzPacket>();

        streamer.PacketReady += (_, e) => packets.Add(e.Packet);

        // Simulate keystrokes — Feed() computes interval from real clock so we
        // call it in rapid succession; sequence ordering is what we validate.
        char[] chars = ['H', 'e', 'l', 'l', 'o', ' ', 'W', 'o', 'r', 'd'];
        for (int i = 0; i < chars.Length; i++)
            streamer.Feed(chars[i]);

        Assert.Equal(chars.Length, packets.Count);

        for (int i = 0; i < packets.Count; i++)
        {
            Assert.Equal(SemaBuzzPacketType.Char, packets[i].Type);
            Assert.Equal(chars[i], packets[i].Character);
            Assert.InRange(packets[i].Intensity, (byte)0, (byte)255);

            if (i > 0)
                Assert.True(packets[i].SeqNum > packets[i - 1].SeqNum,
                    $"Sequence number did not increase at index {i}");
        }
    }
}

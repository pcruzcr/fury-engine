using System.Security.Cryptography;

namespace Fury.Core.Net;

/// <summary>
/// Directional session keys of one peer: what it signs with and what it verifies against.
/// The two directions use different keys, so a datagram captured from one peer and reflected
/// back to it fails authentication (a single shared key let a third party echo our own packets
/// back as "remote input").
/// </summary>
public sealed class SessionKeys : IDisposable
{
    public SessionKeys(byte[] sendKey, byte[] receiveKey)
    {
        SendKey = sendKey;
        ReceiveKey = receiveKey;
    }

    public byte[] SendKey { get; }

    public byte[] ReceiveKey { get; }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(SendKey);
        CryptographicOperations.ZeroMemory(ReceiveKey);
    }
}

/// <summary>
/// Session key agreement for the netcode: ephemeral ECDH P-256 + HKDF-SHA256. Peers exchange
/// public blobs only; private keys never leave the endpoint. New keypair per session, no static
/// keys in the binary (ADR-004). The HKDF salt binds the whole transcript (both public keys in
/// canonical order) and an optional matchmaker-issued token: a man-in-the-middle that does not
/// know the token derives different keys and every packet is rejected.
/// </summary>
public static class SessionCrypto
{
    private const int PublicKeyBytes = 65; // uncompressed P-256 point: 0x04 | X(32) | Y(32)
    private const int KeyBytes = 32;
    private static readonly byte[] Label = "FURY-NET-v2"u8.ToArray();
    private static readonly byte[] Info = "fury-session-keys/v2"u8.ToArray();

    public static ECDiffieHellman CreateKeyPair() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>Exportable public blob to send to the peer.</summary>
    public static byte[] ExportPublicKey(ECDiffieHellman keyPair)
    {
        ArgumentNullException.ThrowIfNull(keyPair);
        var parameters = keyPair.ExportParameters(false);
        var blob = new byte[PublicKeyBytes];
        blob[0] = 0x04;
        parameters.Q.X!.AsSpan().CopyTo(blob.AsSpan(1));
        parameters.Q.Y!.AsSpan().CopyTo(blob.AsSpan(33));
        return blob;
    }

    /// <summary>
    /// Imports a peer public blob and derives the two directional keys.
    /// </summary>
    /// <param name="matchToken">Optional shared secret from the matchmaker, mixed into the salt.</param>
    /// <exception cref="InvalidDataException">Malformed or invalid peer key (including our own key echoed back).</exception>
    public static SessionKeys DeriveSessionKeys(ECDiffieHellman keyPair, ReadOnlySpan<byte> peerPublicKey, ReadOnlySpan<byte> matchToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyPair);
        if (peerPublicKey.Length != PublicKeyBytes || peerPublicKey[0] != 0x04)
        {
            throw new InvalidDataException("Malformed peer public key.");
        }

        byte[] own = ExportPublicKey(keyPair);
        int order = own.AsSpan().SequenceCompareTo(peerPublicKey);
        if (order == 0) throw new InvalidDataException("Peer public key equals our own (reflection).");

        byte[] secret;
        try
        {
            var q = new ECPoint
            {
                X = peerPublicKey[1..33].ToArray(),
                Y = peerPublicKey[33..65].ToArray(),
            };
            using var peer = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = q });
            secret = keyPair.DeriveRawSecretAgreement(peer.PublicKey);
        }
        catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException or ArgumentException)
        {
            throw new InvalidDataException("Peer public key is not a valid P-256 point.", e);
        }

        try
        {
            ReadOnlySpan<byte> low = order < 0 ? own : peerPublicKey;
            ReadOnlySpan<byte> high = order < 0 ? peerPublicKey : own;

            using var transcript = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            transcript.AppendData(Label);
            transcript.AppendData(low);
            transcript.AppendData(high);
            transcript.AppendData(matchToken);
            byte[] salt = transcript.GetHashAndReset();

            byte[] okm = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, KeyBytes * 2, salt, Info);
            try
            {
                byte[] lowToHigh = okm[..KeyBytes];
                byte[] highToLow = okm[KeyBytes..];
                // The peer with the lexicographically lower public key signs with the first key.
                return order < 0 ? new SessionKeys(lowToHigh, highToLow) : new SessionKeys(highToLow, lowToHigh);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(okm);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret); // raw ECDH secret must not linger
        }
    }
}

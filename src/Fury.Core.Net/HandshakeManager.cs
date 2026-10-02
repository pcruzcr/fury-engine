using System.Security.Cryptography;

namespace Fury.Core.Net;

/// <summary>
/// One side of the initial key exchange (ephemeral ECDH P-256 + HKDF-SHA256, see
/// <see cref="SessionCrypto"/>). Pass the matchmaker-issued <c>matchToken</c> to authenticate the
/// session: without it the exchange is encrypted-grade but anonymous.
/// </summary>
public sealed class HandshakeManager : IDisposable
{
    private readonly ECDiffieHellman _keyPair;
    private readonly byte[] _matchToken;
    private SessionKeys? _keys;

    public HandshakeManager(ReadOnlySpan<byte> matchToken = default)
    {
        _keyPair = SessionCrypto.CreateKeyPair();
        _matchToken = matchToken.ToArray();
    }

    /// <summary>Gets the public key to send to the peer.</summary>
    public byte[] GetPublicKey() => SessionCrypto.ExportPublicKey(_keyPair);

    /// <summary>
    /// Completes the handshake by deriving the directional session keys from the peer's public key.
    /// </summary>
    /// <exception cref="InvalidDataException">Invalid peer key.</exception>
    public void CompleteHandshake(ReadOnlySpan<byte> peerPublicKey)
    {
        if (_keys != null) return;
        _keys = SessionCrypto.DeriveSessionKeys(_keyPair, peerPublicKey, _matchToken);
    }

    /// <summary>Directional keys; null until the handshake completed.</summary>
    public SessionKeys? SessionKeys => _keys;

    /// <summary>True when the handshake is complete.</summary>
    public bool IsComplete => _keys != null;

    public void Dispose()
    {
        _keyPair.Dispose();
        _keys?.Dispose();
        CryptographicOperations.ZeroMemory(_matchToken);
    }
}

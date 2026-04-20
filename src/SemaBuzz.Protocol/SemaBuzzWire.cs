using System.Net;
using System.Net.Sockets;

namespace SemaBuzz.Protocol;

/// <summary>
/// Connection state of the SemaBuzz wire.
/// </summary>
public enum SemaBuzzWireState
{
    Cold,       // No connection
    Warming,    // Handshake in progress
    Live,       // Connected and transmitting
    Secured,    // Encrypted handshake complete
    Dead,       // Connection lost
}

/// <summary>
/// Event args carrying a received SemaBuzzPacket off the wire.
/// </summary>
public sealed class SemaBuzzPacketEventArgs(SemaBuzzPacket packet) : EventArgs
{
    public SemaBuzzPacket Packet { get; } = packet;
}

/// <summary>
/// Event args for wire state changes.
/// </summary>
public sealed class SemaBuzzWireStateEventArgs(SemaBuzzWireState state, string? message = null) : EventArgs
{
    public SemaBuzzWireState State { get; } = state;
    public string? Message { get; } = message;
}

/// <summary>
/// Event args carrying peer identity metadata (handle + optional avatar PNG).
/// </summary>
public sealed class SemaBuzzMetadataEventArgs(string handle, byte[]? avatarPng) : EventArgs
{
    public string  Handle    { get; } = handle;
    public byte[]? AvatarPng { get; } = avatarPng;
}

/// <summary>
/// Event args carrying one chunk of a peer-sent image.
/// </summary>
public sealed class SemaBuzzImageChunkEventArgs(
    byte transferId, ushort chunkIdx, ushort total, byte[] data) : EventArgs
{
    public byte   TransferId { get; } = transferId;
    public ushort ChunkIdx   { get; } = chunkIdx;
    public ushort Total      { get; } = total;
    public byte[] Data       { get; } = data;
}

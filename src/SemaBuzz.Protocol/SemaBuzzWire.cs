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
/// Online status for a connected peer.
/// </summary>
public enum SemaBuzzStatus : byte
{
    Available = 0,
    Away = 1,
    Busy = 2,
}

/// <summary>
/// Event args carrying peer identity metadata (handle + optional avatar PNG + status).
/// </summary>
public sealed class SemaBuzzMetadataEventArgs(
    string handle, byte[]? avatarPng,
    SemaBuzzStatus status = SemaBuzzStatus.Available, string statusMessage = "") : EventArgs
{
    public string Handle { get; } = handle;
    public byte[]? AvatarPng { get; } = avatarPng;
    public SemaBuzzStatus Status { get; } = status;
    public string StatusMessage { get; } = statusMessage;
}

/// <summary>
/// Event args carrying a URL pushed from the remote peer.
/// </summary>
public sealed class SemaBuzzUrlPushEventArgs(string url) : EventArgs
{
    public string Url { get; } = url;
}

/// <summary>
/// Event args carrying a whiteboard draw event from the remote peer.
/// </summary>
public sealed class SemaBuzzDrawEventArgs(SemaBuzzDrawEvent drawEvent) : EventArgs
{
    public SemaBuzzDrawEvent DrawEvent { get; } = drawEvent;
}

/// <summary>
/// Event args carrying one chunk of a peer-sent image.
/// </summary>
public sealed class SemaBuzzImageChunkEventArgs(
    byte transferId, ushort chunkIdx, ushort total, byte[] data) : EventArgs
{
    public byte TransferId { get; } = transferId;
    public ushort ChunkIdx { get; } = chunkIdx;
    public ushort Total { get; } = total;
    public byte[] Data { get; } = data;
}

/// <summary>
/// Event args for an incoming file-transfer offer.
/// </summary>
public sealed class SemaBuzzFileOfferEventArgs(
    byte transferId, string filename, long fileSize, ushort totalChunks, byte[] sha256) : EventArgs
{
    public byte TransferId { get; } = transferId;
    public string Filename { get; } = filename;
    public long FileSize { get; } = fileSize;
    public ushort TotalChunks { get; } = totalChunks;
    public byte[] Sha256 { get; } = sha256;
}

/// <summary>
/// Event args for one received chunk of an in-progress file transfer.
/// </summary>
public sealed class SemaBuzzFileChunkEventArgs(
    byte transferId, ushort chunkIdx, byte[] data) : EventArgs
{
    public byte TransferId { get; } = transferId;
    public ushort ChunkIdx { get; } = chunkIdx;
    public byte[] Data { get; } = data;
}

/// <summary>
/// Event args for file-transfer control signals (Accept, Reject, Complete, Cancel).
/// </summary>
public sealed class SemaBuzzFileControlEventArgs(byte transferId) : EventArgs
{
    public byte TransferId { get; } = transferId;
}

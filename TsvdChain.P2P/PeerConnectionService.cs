using System.Collections.Concurrent;

namespace TsvdChain.P2P;

/// <summary>
/// Singleton service for tracking connected peer connections.
/// Hub instances are transient, so peer state must be stored here.
/// </summary>
public sealed class PeerConnectionService
{
    private readonly ConcurrentDictionary<string, PeerInfo> _peers = new();

    /// <summary>
    /// Information about a connected peer.
    /// </summary>
    public sealed record PeerInfo(string ConnectionId, string NodeId, DateTime ConnectedAt);

    /// <summary>
    /// Registers a new peer connection.
    /// </summary>
    public bool AddPeer(string connectionId, string nodeId)
    {
        var peer = new PeerInfo(connectionId, nodeId, DateTime.UtcNow);
        return _peers.TryAdd(connectionId, peer);
    }

    /// <summary>
    /// Removes a peer connection.
    /// </summary>
    public bool RemovePeer(string connectionId)
    {
        return _peers.TryRemove(connectionId, out _);
    }

    /// <summary>
    /// Gets all connected peers.
    /// </summary>
    public IReadOnlyCollection<PeerInfo> GetAllPeers() => _peers.Values.ToList().AsReadOnly();

    /// <summary>
    /// Gets peer count.
    /// </summary>
    public int PeerCount => _peers.Count;
}

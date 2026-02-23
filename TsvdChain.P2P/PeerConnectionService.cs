using System.Collections.Concurrent;

namespace TsvdChain.P2P;

/// <summary>
/// Singleton service for tracking connected peer connections.
/// Hub instances are transient, so peer state must be stored here.
/// </summary>
public sealed class PeerConnectionService
{
    private readonly ConcurrentDictionary<string, PeerInfo> _peers = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _syncGroups = new();

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

    /// <summary>
    /// Adds a peer to a sync group for blockchain synchronization.
    /// </summary>
    public void AddToSyncGroup(string connectionId, string groupName)
    {
        _syncGroups.AddOrUpdate(
            groupName,
            _ => new HashSet<string> { connectionId },
            (_, set) => { set.Add(connectionId); return set; });
    }

    /// <summary>
    /// Removes a peer from a sync group.
    /// </summary>
    public void RemoveFromSyncGroup(string connectionId, string groupName)
    {
        if (_syncGroups.TryGetValue(groupName, out var set))
        {
            set.Remove(connectionId);
        }
    }

    /// <summary>
    /// Gets all connection IDs in a sync group.
    /// </summary>
    public IEnumerable<string> GetSyncGroupMembers(string groupName)
    {
        if (_syncGroups.TryGetValue(groupName, out var set))
        {
            return set.ToList();
        }
        return Enumerable.Empty<string>();
    }
}

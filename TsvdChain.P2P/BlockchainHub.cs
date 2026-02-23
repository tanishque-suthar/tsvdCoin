using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using TsvdChain.Core.Blockchain;

namespace TsvdChain.P2P;

/// <summary>
/// SignalR Hub for blockchain P2P communication.
/// Note: Hub instances are transient - do not store state here.
/// Use PeerConnectionService singleton for peer tracking.
/// </summary>
public class BlockchainHub : Hub
{
    private readonly PeerConnectionService _peerService;
    private readonly ILogger<BlockchainHub> _logger;
    
    // Using IHubContext for broadcasting (not Clients.All inside Hub)
    private readonly IHubContext<BlockchainHub> _hubContext;

    public BlockchainHub(
        PeerConnectionService peerService,
        IHubContext<BlockchainHub> hubContext,
        ILogger<BlockchainHub> logger)
    {
        _peerService = peerService;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Called when a new peer connects.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var nodeId = Context.GetHttpContext()?.Request.Query["nodeId"].ToString() 
            ?? Context.ConnectionId;
        
        _peerService.AddPeer(Context.ConnectionId, nodeId);
        _logger.LogInformation("Peer connected: {NodeId}, Total peers: {Count}", 
            nodeId, _peerService.PeerCount);
        
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a peer disconnects.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _peerService.RemovePeer(Context.ConnectionId);
        _logger.LogInformation("Peer disconnected: {ConnectionId}, Total peers: {Count}", 
            Context.ConnectionId, _peerService.PeerCount);
        
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Register this node for blockchain sync at a specific block height.
    /// </summary>
    public async Task JoinSyncGroup(int blockHeight)
    {
        var groupName = $"sync-{blockHeight}";
        _peerService.AddToSyncGroup(Context.ConnectionId, groupName);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Peer {ConnectionId} joined sync group {Group}", 
            Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Broadcast a new block to all connected peers.
    /// </summary>
    public async Task BroadcastBlock(Block block)
    {
        await _hubContext.Clients.All.SendAsync("ReceiveBlock", block);
        _logger.LogDebug("Broadcast block {Index} to all peers", block.Index);
    }

    /// <summary>
    /// Request blockchain sync from peers.
    /// </summary>
    public async Task RequestChainSync()
    {
        await Clients.Others.SendAsync("RequestChain");
        _logger.LogInformation("Requested chain sync from peers");
    }

    /// <summary>
    /// Send the current blockchain state to the requesting peer.
    /// </summary>
    public async Task SendChain(IEnumerable<Block> chain)
    {
        await Clients.Caller.SendAsync("ReceiveChain", chain);
    }

    /// <summary>
    /// Announce this node's latest block height.
    /// </summary>
    public async Task AnnounceBlockHeight(int height)
    {
        await Clients.Others.SendAsync("PeerBlockHeight", Context.ConnectionId, height);
    }
}

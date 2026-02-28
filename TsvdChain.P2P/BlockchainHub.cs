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
    private readonly IBlockchainNodeService _nodeService;

    public BlockchainHub(
        PeerConnectionService peerService,
        IBlockchainNodeService nodeService,
        ILogger<BlockchainHub> logger)
    {
        _peerService = peerService;
        _nodeService = nodeService;
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
    /// Submit a block from a peer to this node. If accepted, rebroadcast to other peers.
    /// If rejected, request the submitter's full chain to resolve the fork.
    /// </summary>
    public async Task SubmitBlock(Block block)
    {
        if (await _nodeService.TryAcceptBlockAsync(block))
        {
            // Rebroadcast to all peers except the one who sent it.
            await Clients.Others.SendAsync("ReceiveBlock", block);
            _logger.LogInformation("Accepted and rebroadcast block {Index} from peer {ConnectionId}",
                block.Index, Context.ConnectionId);
        }
        else
        {
            // Block rejected — we may be behind. Ask the submitter for their full chain.
            _logger.LogWarning("Block {Index} from peer {ConnectionId} rejected; requesting their chain",
                block.Index, Context.ConnectionId);
            await Clients.Caller.SendAsync("RequestChain");
        }
    }

    /// <summary>
    /// Send the current blockchain state to the requesting peer.
    /// Called by a peer (or outbound client) to get our chain.
    /// </summary>
    public async Task SendChain()
    {
        var chain = _nodeService.GetChain();
        await Clients.Caller.SendAsync("ReceiveChain", chain);
    }
}
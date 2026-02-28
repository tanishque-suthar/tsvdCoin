namespace TsvdChain.Core.Blockchain;

public interface IBlockchainNodeService
{
    IReadOnlyList<Block> GetChain();
    Block? GetLatestBlock();
    Task<Block> MineBlockAsync(CancellationToken cancellationToken = default);
    Task<bool> TryAcceptBlockAsync(Block block, CancellationToken cancellationToken = default);
    Task<bool> TryReplaceChainAsync(IEnumerable<Block> remoteChain, CancellationToken cancellationToken = default);
}
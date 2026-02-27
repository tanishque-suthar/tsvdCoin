using TsvdChain.Core.Blockchain;

namespace TsvdChain.P2P;

public interface IBlockchainNodeService
{
    IReadOnlyList<Block> GetChain();
    Task<bool> TryAcceptBlockAsync(Block block, CancellationToken cancellationToken = default);
}


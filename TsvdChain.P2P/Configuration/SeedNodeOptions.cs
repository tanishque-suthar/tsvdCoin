namespace TsvdChain.P2P.Configuration;

/// <summary>
/// Configuration options for seed nodes - initial entry points for peer discovery.
/// </summary>
public sealed class SeedNodeOptions
{
    public const string SectionName = "SeedNodes";
    
    /// <summary>
    /// List of seed node URLs for initial peer discovery.
    /// </summary>
    public List<string> Nodes { get; init; } = new();
    
    /// <summary>
    /// Whether to use seed nodes for discovery.
    /// </summary>
    public bool EnableSeedNodes { get; init; } = true;
}

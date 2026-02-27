using System.Collections.Generic;
using System.Linq;

namespace TsvdChain.Core.Hashing;

/// <summary>
/// Simple Merkle tree builder that computes a Merkle root from a sequence of hex leaf hashes.
/// - If the list is empty, returns SHA256("").
/// - If an odd number of leaves, the last leaf is duplicated for that level.
/// Uses Sha256Hasher for hashing (keeps caller's zero-allocation hashing).
/// </summary>
public static class MerkleTree
{
    /// <summary>
    /// Computes the Merkle root given leaf hashes (hex strings).
    /// </summary>
    public static string ComputeMerkleRoot(IEnumerable<string>? leafHashes)
    {
        var leaves = leafHashes?.Where(h => !string.IsNullOrWhiteSpace(h)).ToList() ?? new List<string>();

        if (leaves.Count == 0)
        {
            return Sha256Hasher.ComputeHashString(string.Empty);
        }

        while (leaves.Count > 1)
        {
            var nextLevel = new List<string>((leaves.Count + 1) / 2);

            for (int i = 0; i < leaves.Count; i += 2)
            {
                var left = leaves[i];
                var right = (i + 1 < leaves.Count) ? leaves[i + 1] : left; // duplicate last if odd
                var parent = Sha256Hasher.ComputeHashString(left + right);
                nextLevel.Add(parent);
            }

            leaves = nextLevel;
        }

        return leaves[0];
    }
}
namespace TsvdChain.Api.Dto;

/// <summary>
/// DTO for signing and sending a transaction using this node's wallet.
/// </summary>
public sealed record SendTxDto(string To, long Amount);
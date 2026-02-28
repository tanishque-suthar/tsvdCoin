namespace TsvdChain.Api.Dto;

/// <summary>
/// DTO for submitting a pre-signed transaction from an external wallet.
/// </summary>
public sealed record TxDto(string From, string To, long Amount, long Timestamp, string Signature, string Id);

/// <summary>
/// DTO for signing and sending a transaction using this node's wallet.
/// </summary>
public sealed record SendTxDto(string To, long Amount);
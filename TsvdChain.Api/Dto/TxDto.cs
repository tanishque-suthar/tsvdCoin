namespace TsvdChain.Api.Dto;

public sealed record TxDto(string From, string To, long Amount, string? Signature);
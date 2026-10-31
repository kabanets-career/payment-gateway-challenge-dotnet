using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Clients.Bank;

public sealed record BankPaymentResponse
{
    [JsonPropertyName("authorized")]
    public bool Authorized { get; init; }

    [JsonPropertyName("authorization_code")]
    public string AuthorizationCode { get; init; } = string.Empty;
}
using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Clients.Bank;

public sealed record BankPaymentRequest
{
    [JsonPropertyName("card_number")]
    public string CardNumber { get; init; } = string.Empty;

    [JsonPropertyName("expiry_date")]
    public string ExpiryDate { get; init; } = string.Empty;

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; init; }

    [JsonPropertyName("cvv")]
    public string Cvv { get; init; } = string.Empty;

    public override string ToString()
    {
        return $"{nameof(BankPaymentRequest)} {{ CardNumber = [REDACTED], "
            + "ExpiryDate = [REDACTED], "
            + $"Currency = {Currency}, Amount = {Amount}, Cvv = [REDACTED] }}";
    }
}
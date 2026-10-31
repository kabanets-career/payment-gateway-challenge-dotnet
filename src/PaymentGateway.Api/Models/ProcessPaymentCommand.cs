namespace PaymentGateway.Api.Models;

public sealed record ProcessPaymentCommand
{
    public required string CardNumber { get; init; }
    public required int ExpiryMonth { get; init; }
    public required int ExpiryYear { get; init; }
    public required string Currency { get; init; }
    public required int Amount { get; init; }
    public required string Cvv { get; init; }

    public override string ToString()
    {
        return $"{nameof(ProcessPaymentCommand)} {{ CardNumber = [REDACTED], "
            + "ExpiryMonth = [REDACTED], ExpiryYear = [REDACTED], "
            + $"Currency = {Currency}, Amount = {Amount}, Cvv = [REDACTED] }}";
    }
}
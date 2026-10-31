namespace PaymentGateway.Api.Models.Requests;

public sealed record PostPaymentRequest
{
    public string? CardNumber { get; init; }
    public int ExpiryMonth { get; init; }
    public int ExpiryYear { get; init; }
    public string? Currency { get; init; }
    public int? Amount { get; init; }
    public string? Cvv { get; init; }

    public override string ToString()
    {
        return $"{nameof(PostPaymentRequest)} {{ CardNumber = [REDACTED], "
            + "ExpiryMonth = [REDACTED], ExpiryYear = [REDACTED], "
            + $"Currency = {Currency}, Amount = {Amount}, Cvv = [REDACTED] }}";
    }

    public ProcessPaymentCommand ToCommand()
    {
        return new ProcessPaymentCommand
        {
            CardNumber = CardNumber ?? throw new InvalidOperationException("CardNumber must be provided."),
            ExpiryMonth = ExpiryMonth,
            ExpiryYear = ExpiryYear,
            Currency = Currency ?? throw new InvalidOperationException("Currency must be provided."),
            Amount = Amount ?? throw new InvalidOperationException("Amount must be provided."),
            Cvv = Cvv ?? throw new InvalidOperationException("Cvv must be provided.")
        };
    }
}
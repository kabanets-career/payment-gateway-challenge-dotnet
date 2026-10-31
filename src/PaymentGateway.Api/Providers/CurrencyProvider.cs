namespace PaymentGateway.Api.Providers;

public class CurrencyProvider : ICurrencyProvider
{
    public IReadOnlyCollection<string> SupportedCurrencies { get; } =
        Array.AsReadOnly(new[] { "GBP", "USD", "EUR" });
}

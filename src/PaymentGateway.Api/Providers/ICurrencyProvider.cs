namespace PaymentGateway.Api.Providers;

public interface ICurrencyProvider
{
    IReadOnlyCollection<string> SupportedCurrencies { get; }
}

using System.Globalization;

using PaymentGateway.Api.Providers;

namespace PaymentGateway.Api.Tests.Unit.Providers;

public class CurrencyProviderTests
{
    private readonly ICurrencyProvider _provider = new CurrencyProvider();

    [Fact]
    public void ProvidesBetweenOneAndThreeSupportedCurrencies()
    {
        Assert.InRange(_provider.SupportedCurrencies.Count, 1, 3);
    }

    [Fact]
    public void ProvidesDistinctCurrencies()
    {
        var currencies = _provider.SupportedCurrencies;

        Assert.Equal(currencies.Count, currencies.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ProvidesIsoCurrencyCodes()
    {
        var isoCurrencyCodes = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(culture => new RegionInfo(culture.Name).ISOCurrencySymbol)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(_provider.SupportedCurrencies, currency =>
        {
            Assert.Matches("\\A[A-Z]{3}\\z", currency);
            Assert.Contains(currency, isoCurrencyCodes);
        });
    }
}

using System.Text.Json;

using FluentValidation.TestHelper;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Providers;
using PaymentGateway.Api.Validators;

namespace PaymentGateway.Api.Tests.Unit.Validators;

public class PostPaymentRequestValidatorTests
{
    private readonly TestTimeProvider _timeProvider = new();
    private readonly ICurrencyProvider _currencyProvider = new TestCurrencyProvider();

    [Fact]
    public void AcceptsAValidRequest()
    {
        CreateValidator().TestValidate(CreateValidRequest()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void RejectsMissingFields()
    {
        var result = CreateValidator().TestValidate(new PostPaymentRequest());

        result.ShouldHaveValidationErrorFor(request => request.CardNumber);
        result.ShouldHaveValidationErrorFor(request => request.ExpiryMonth);
        result.ShouldHaveValidationErrorFor(request => request.ExpiryYear);
        result.ShouldHaveValidationErrorFor(request => request.Currency);
        result.ShouldHaveValidationErrorFor(request => request.Amount);
        result.ShouldHaveValidationErrorFor(request => request.Cvv);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("              ")]
    [InlineData("1234567890123")]
    [InlineData("12345678901234567890")]
    [InlineData("1234567890123a")]
    [InlineData("1234 5678901234")]
    [InlineData("+1234567890123")]
    [InlineData("１２３４５６７８９０１２３４")]
    [InlineData("12345678901234\n")]
    public void RejectsInvalidCardNumbers(string? cardNumber)
    {
        var request = CreateValidRequest(cardNumber: cardNumber);

        CreateValidator().TestValidate(request)
            .ShouldHaveValidationErrorFor(payment => payment.CardNumber).Only();
    }

    [Theory]
    [InlineData("00000000000000")]
    [InlineData("1234567890123456")]
    [InlineData("1234567890123456789")]
    public void AcceptsNumericCardNumbersAtLengthBoundaries(string cardNumber)
    {
        var request = CreateValidRequest(cardNumber: cardNumber);

        CreateValidator().TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(int.MaxValue)]
    public void RejectsInvalidExpiryMonths(int month)
    {
        var request = CreateValidRequest(expiryMonth: month);

        CreateValidator().TestValidate(request)
            .ShouldHaveValidationErrorFor(payment => payment.ExpiryMonth);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2029)]
    public void RejectsMissingOrPastExpiryYears(int year)
    {
        var request = CreateValidRequest(expiryMonth: 12, expiryYear: year);

        CreateValidator().TestValidate(request)
            .ShouldHaveValidationErrorFor(payment => payment.ExpiryYear);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public void RejectsPastAndCurrentMonthsInTheCurrentYear(int month)
    {
        var request = CreateValidRequest(expiryMonth: month, expiryYear: 2030);

        CreateValidator().TestValidate(request)
            .ShouldHaveValidationErrorFor(payment => payment.ExpiryYear).Only();
    }

    [Theory]
    [InlineData(11, 2030)]
    [InlineData(12, 2030)]
    [InlineData(1, 2031)]
    [InlineData(12, 2031)]
    public void AcceptsFutureExpiryMonthAndYearCombinations(int month, int year)
    {
        var request = CreateValidRequest(expiryMonth: month, expiryYear: year);

        CreateValidator().TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void AcceptsJanuaryOfNextYearInDecember()
    {
        _timeProvider.UtcNow = new DateTimeOffset(2030, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var request = CreateValidRequest(expiryMonth: 1, expiryYear: 2031);

        CreateValidator().TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EvaluatesTheCurrentDateOnEachValidation()
    {
        var validator = CreateValidator();
        var request = CreateValidRequest(expiryMonth: 11, expiryYear: 2030);

        validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();

        _timeProvider.UtcNow = new DateTimeOffset(2030, 11, 1, 0, 0, 0, TimeSpan.Zero);

        validator.TestValidate(request)
            .ShouldHaveValidationErrorFor(payment => payment.ExpiryYear).Only();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AA")]
    [InlineData("AAAA")]
    [InlineData("AAA\n")]
    public void RejectsMissingOrMalformedCurrencies(string? currency)
    {
        var request = CreateValidRequest(currency: currency);

        CreateValidator().TestValidate(request)
            .ShouldHaveValidationErrorFor(payment => payment.Currency).Only();
    }

    [Fact]
    public void AcceptsCurrenciesSuppliedByTheProvider()
    {
        var validator = CreateValidator();

        foreach (var currency in _currencyProvider.SupportedCurrencies)
        {
            var request = CreateValidRequest(currency: currency);

            validator.TestValidate(request).ShouldNotHaveAnyValidationErrors();
        }
    }

    [Fact]
    public void RejectsCurrenciesNotSuppliedByTheProvider()
    {
        var request = CreateValidRequest(currency: "ZZZ");

        CreateValidator().TestValidate(request)
            .ShouldHaveValidationErrorFor(request => request.Currency)
            .WithErrorMessage($"Currency must be one of {string.Join(", ", _currencyProvider.SupportedCurrencies)}.")
            .Only();
    }

    [Fact]
    public void RejectsCurrenciesWithDifferentCasingFromTheProvider()
    {
        var request = CreateValidRequest(currency: _currencyProvider.SupportedCurrencies.First().ToLowerInvariant());

        CreateValidator().TestValidate(request)
            .ShouldHaveValidationErrorFor(payment => payment.Currency).Only();
    }

    [Fact]
    public void RejectsMissingAmount()
    {
        var request = CreateValidRequest(amount: null);

        CreateValidator().TestValidate(request)
            .ShouldHaveValidationErrorFor(payment => payment.Amount).Only();
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1050)]
    [InlineData(int.MaxValue)]
    public void AcceptsIntegerAmountsWithoutAnAdditionalRangeRestriction(int amount)
    {
        var request = CreateValidRequest(amount: amount);

        CreateValidator().TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void RejectsFractionalAmountsDuringJsonDeserialization()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PostPaymentRequest>(
            "{\"amount\": 10.5}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("12a")]
    [InlineData("1 23")]
    [InlineData("+123")]
    [InlineData("１２３")]
    [InlineData("123\n")]
    public void RejectsInvalidCvvs(string? cvv)
    {
        var request = CreateValidRequest(cvv: cvv);

        CreateValidator().TestValidate(request)
            .ShouldHaveValidationErrorFor(payment => payment.Cvv).Only();
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234")]
    [InlineData("000")]
    [InlineData("0000")]
    public void AcceptsThreeOrFourDigitCvvsIncludingLeadingZeroes(string cvv)
    {
        var request = CreateValidRequest(cvv: cvv);

        CreateValidator().TestValidate(request).ShouldNotHaveAnyValidationErrors();
    }

    private PostPaymentRequestValidator CreateValidator() => new(_timeProvider, _currencyProvider);

    private static PostPaymentRequest CreateValidRequest(
        string? cardNumber = "1234567890123456",
        int expiryMonth = 1,
        int expiryYear = 2031,
        string? currency = "AAA",
        int? amount = 1050,
        string? cvv = "012") => new()
    {
        CardNumber = cardNumber,
        ExpiryMonth = expiryMonth,
        ExpiryYear = expiryYear,
        Currency = currency,
        Amount = amount,
        Cvv = cvv
    };

    private sealed class TestCurrencyProvider : ICurrencyProvider
    {
        public IReadOnlyCollection<string> SupportedCurrencies { get; } = new[] { "AAA", "BBB" };
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } =
            new(2030, 10, 15, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
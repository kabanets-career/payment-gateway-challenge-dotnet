using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Tests.Unit.Models.Requests;

public class PostPaymentRequestTests
{
    [Fact]
    public void ToStringRedactsSensitiveData()
    {
        var request = new PostPaymentRequest
        {
            CardNumber = "2222405343240042",
            ExpiryMonth = 9,
            ExpiryYear = 2099,
            Currency = "GBP",
            Amount = 1050,
            Cvv = "987"
        };

        var text = request.ToString();

        Assert.Contains("CardNumber = [REDACTED]", text);
        Assert.Contains("Cvv = [REDACTED]", text);
        Assert.Contains("ExpiryMonth = [REDACTED], ExpiryYear = [REDACTED]", text);
        Assert.DoesNotContain(request.CardNumber, text);
        Assert.DoesNotContain("0042", text);
        Assert.DoesNotContain(request.Cvv, text);
        Assert.DoesNotContain("2099", text);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1)]
    public void MapsAllFieldsToProcessPaymentCommand(int amount)
    {
        var request = new PostPaymentRequest
        {
            CardNumber = "2222405343240042",
            ExpiryMonth = 4,
            ExpiryYear = 2030,
            Currency = "GBP",
            Amount = amount,
            Cvv = "012"
        };

        ProcessPaymentCommand command = request.ToCommand();

        Assert.Equal(request.CardNumber, command.CardNumber);
        Assert.Equal(request.ExpiryMonth, command.ExpiryMonth);
        Assert.Equal(request.ExpiryYear, command.ExpiryYear);
        Assert.Equal(request.Currency, command.Currency);
        Assert.Equal(amount, command.Amount);
        Assert.Equal(request.Cvv, command.Cvv);
    }

    [Theory]
    [InlineData(null, "GBP", 100, "123")]
    [InlineData("2222405343248877", null, 100, "123")]
    [InlineData("2222405343248877", "GBP", null, "123")]
    [InlineData("2222405343248877", "GBP", 100, null)]
    public void ThrowsWhenNullableFieldIsMissing(string? cardNumber, string? currency, int? amount, string? cvv)
    {
        var request = new PostPaymentRequest
        {
            CardNumber = cardNumber,
            ExpiryMonth = 4,
            ExpiryYear = 2030,
            Currency = currency,
            Amount = amount,
            Cvv = cvv
        };

        Assert.Throws<InvalidOperationException>(() => request.ToCommand());
    }

    [Theory]
    [InlineData(null, 2030)]
    [InlineData(4, null)]
    public void ThrowsWhenExpiryFieldIsMissing(int? month, int? year)
    {
        var request = new PostPaymentRequest
        {
            CardNumber = "2222405343248877", ExpiryMonth = month, ExpiryYear = year,
            Currency = "GBP", Amount = 100, Cvv = "123"
        };

        Assert.Throws<InvalidOperationException>(() => request.ToCommand());
    }
}

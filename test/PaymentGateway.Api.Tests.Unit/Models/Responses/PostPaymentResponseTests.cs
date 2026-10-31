using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Tests.Unit.Models.Responses;

public class PostPaymentResponseTests
{
    [Theory]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Declined)]
    public void MapsPaymentToResponse(PaymentStatus status)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Status = status,
            AuthorizationCode = "AUTH-123",
            CardNumberLastFour = "0042",
            ExpiryMonth = 9,
            ExpiryYear = 2030,
            Currency = "GBP",
            Amount = 1050
        };

        var result = PostPaymentResponse.ToResponse(payment);

        Assert.Equal(payment.Id, result.Id);
        Assert.Equal(payment.Status, result.Status);
        Assert.Equal(payment.CardNumberLastFour, result.CardNumberLastFour);
        Assert.Equal(payment.ExpiryMonth, result.ExpiryMonth);
        Assert.Equal(payment.ExpiryYear, result.ExpiryYear);
        Assert.Equal(payment.Currency, result.Currency);
        Assert.Equal(payment.Amount, result.Amount);
    }
}

using PaymentGateway.Api.Clients.Bank;

namespace PaymentGateway.Api.Tests.Unit.Clients.Bank;

public class BankPaymentRequestTests
{
    [Fact]
    public void ToStringRedactsSensitiveData()
    {
        var request = new BankPaymentRequest
        {
            CardNumber = "2222405343240042",
            ExpiryDate = "09/2099",
            Currency = "GBP",
            Amount = 1050,
            Cvv = "987"
        };

        var text = request.ToString();

        Assert.Contains("CardNumber = [REDACTED]", text);
        Assert.Contains("Cvv = [REDACTED]", text);
        Assert.Contains("ExpiryDate = [REDACTED]", text);
        Assert.DoesNotContain(request.CardNumber, text);
        Assert.DoesNotContain("0042", text);
        Assert.DoesNotContain(request.Cvv, text);
        Assert.DoesNotContain(request.ExpiryDate, text);
    }
}
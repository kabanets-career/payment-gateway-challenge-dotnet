using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Tests.Unit.Models;

public class ProcessPaymentCommandTests
{
    [Fact]
    public void ToStringRedactsSensitiveData()
    {
        var command = new ProcessPaymentCommand
        {
            CardNumber = "2222405343240042",
            ExpiryMonth = 9,
            ExpiryYear = 2099,
            Currency = "GBP",
            Amount = 1050,
            Cvv = "987"
        };

        var text = command.ToString();

        Assert.Contains("CardNumber = [REDACTED]", text);
        Assert.Contains("Cvv = [REDACTED]", text);
        Assert.Contains("ExpiryMonth = [REDACTED], ExpiryYear = [REDACTED]", text);
        Assert.DoesNotContain(command.CardNumber, text);
        Assert.DoesNotContain("0042", text);
        Assert.DoesNotContain(command.Cvv, text);
        Assert.DoesNotContain("2099", text);
    }
}
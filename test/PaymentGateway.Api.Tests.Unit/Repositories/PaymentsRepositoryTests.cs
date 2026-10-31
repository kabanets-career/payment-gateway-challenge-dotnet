using PaymentGateway.Api.Models;
using PaymentGateway.Api.Repositories;

namespace PaymentGateway.Api.Tests.Unit.Repositories;

public class PaymentsRepositoryTests
{
    [Fact]
    public void RetrievesPaymentById()
    {
        IPaymentsRepository repository = new PaymentsRepository();
        var payment = new Payment { Id = Guid.NewGuid(), AuthorizationCode = "AUTH-123" };
        repository.Add(new Payment { Id = Guid.NewGuid() });
        repository.Add(payment);

        var result = Assert.IsType<Payment>(repository.Get(payment.Id));

        Assert.Same(payment, result);
        Assert.Equal("AUTH-123", result.AuthorizationCode);
    }

    [Fact]
    public void ReturnsNullForUnknownPayment()
    {
        IPaymentsRepository repository = new PaymentsRepository();
        repository.Add(new Payment { Id = Guid.NewGuid() });

        var result = repository.Get(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task SupportsConcurrentAddsAndReads()
    {
        IPaymentsRepository repository = new PaymentsRepository();
        var payments = Enumerable.Range(0, 1000)
            .Select(_ => new Payment { Id = Guid.NewGuid() })
            .ToArray();

        await Task.WhenAll(payments.Select(payment => Task.Run(() =>
        {
            repository.Add(payment);
            Assert.Same(payment, repository.Get(payment.Id));
        })));

        Assert.All(payments, payment => Assert.Same(payment, repository.Get(payment.Id)));
    }
}

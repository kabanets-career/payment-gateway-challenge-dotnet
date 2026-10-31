using PaymentGateway.Api.Models;
using PaymentGateway.Api.Repositories;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests.Unit.Services;

public class PaymentServiceTests
{
    [Theory]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Declined)]
    [InlineData(PaymentStatus.Rejected)]
    public void ReturnsPaymentFromRepository(PaymentStatus status)
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
        var repository = new StubPaymentsRepository(payment);
        var service = new PaymentService(repository);

        var result = service.Get(payment.Id);

        Assert.Equal(payment.Id, repository.RequestedId);
        Assert.Same(payment, result);
    }

    [Fact]
    public void ReturnsNullWhenRepositoryCannotFindPayment()
    {
        var id = Guid.NewGuid();
        var repository = new StubPaymentsRepository();
        var service = new PaymentService(repository);

        var result = service.Get(id);

        Assert.Null(result);
        Assert.Equal(id, repository.RequestedId);
    }

    private sealed class StubPaymentsRepository : IPaymentsRepository
    {
        private readonly Payment? _payment;

        public StubPaymentsRepository(Payment? payment = null)
        {
            _payment = payment;
        }

        public Guid? RequestedId { get; private set; }

        public void Add(Payment payment)
        {
            throw new NotSupportedException();
        }

        public Payment? Get(Guid id)
        {
            RequestedId = id;
            return _payment;
        }
    }
}

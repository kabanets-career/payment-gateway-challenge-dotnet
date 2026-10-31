using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Services;

public interface IPaymentService
{
    Task<Payment> ProcessAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default);

    Payment? Get(Guid id);
}
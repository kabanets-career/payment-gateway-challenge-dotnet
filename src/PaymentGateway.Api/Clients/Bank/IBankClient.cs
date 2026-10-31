using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Clients.Bank;

public interface IBankClient
{
    Task<Payment> ProcessPaymentAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default);
}
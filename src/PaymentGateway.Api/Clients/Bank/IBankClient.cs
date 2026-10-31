using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Clients.Bank;

public interface IBankClient
{
    Task<BankPaymentResponse> ProcessPaymentAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default);
}
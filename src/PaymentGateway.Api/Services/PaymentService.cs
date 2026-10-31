using PaymentGateway.Api.Clients.Bank;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Repositories;

namespace PaymentGateway.Api.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly IBankClient _bankClient;

    public PaymentService(IPaymentsRepository paymentsRepository, IBankClient bankClient)
    {
        _paymentsRepository = paymentsRepository;
        _bankClient = bankClient;
    }

    public async Task<Payment> ProcessAsync(
        ProcessPaymentCommand command, CancellationToken cancellationToken = default)
    {
        var bankResponse = await _bankClient.ProcessPaymentAsync(command, cancellationToken);
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Status = bankResponse.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined,
            AuthorizationCode = bankResponse.AuthorizationCode,
            CardNumberLastFour = command.CardNumber[^4..],
            ExpiryMonth = command.ExpiryMonth,
            ExpiryYear = command.ExpiryYear,
            Currency = command.Currency,
            Amount = command.Amount
        };

        _paymentsRepository.Add(payment);
        return payment;
    }

    public Payment? Get(Guid id)
    {
        return _paymentsRepository.Get(id);
    }
}
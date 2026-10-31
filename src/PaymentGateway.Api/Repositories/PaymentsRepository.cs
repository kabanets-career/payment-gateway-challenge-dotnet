using System.Collections.Concurrent;

using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Repositories;

public class PaymentsRepository : IPaymentsRepository
{
    private readonly ConcurrentDictionary<Guid, Payment> _payments = new();
    
    public void Add(Payment payment)
    {
        if (!_payments.TryAdd(payment.Id, payment))
        {
            throw new InvalidOperationException("A payment with this ID already exists.");
        }
    }

    public Payment? Get(Guid id)
    {
        return _payments.TryGetValue(id, out var payment) ? payment : null;
    }
}

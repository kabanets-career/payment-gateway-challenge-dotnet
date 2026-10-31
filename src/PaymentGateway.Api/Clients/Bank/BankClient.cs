using System.Globalization;
using System.Net;

using PaymentGateway.Api.Clients.Bank.Exceptions;
using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Clients.Bank;

public class BankClient : IBankClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _paymentsUrl;

    public BankClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _paymentsUrl = configuration["Bank:PaymentsUrl"]
            ?? throw new InvalidOperationException("Bank:PaymentsUrl must be configured.");
    }

    public async Task<Payment> ProcessPaymentAsync(
        ProcessPaymentCommand command, CancellationToken cancellationToken = default)
    {
        var bankRequest = new BankPaymentRequest
        {
            CardNumber = command.CardNumber,
            ExpiryDate = string.Create(CultureInfo.InvariantCulture, $"{command.ExpiryMonth:D2}/{command.ExpiryYear:D4}"),
            Currency = command.Currency,
            Amount = command.Amount,
            Cvv = command.Cvv
        };

        using var client = _httpClientFactory.CreateClient();
        using var response = await client.PostAsJsonAsync(_paymentsUrl, bankRequest, cancellationToken);

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            throw new BankUnavailableException("The bank is unavailable.");
        }

        response.EnsureSuccessStatusCode();

        var bankResponse = await response.Content.ReadFromJsonAsync<BankPaymentResponse>(cancellationToken)
            ?? throw new InvalidOperationException("The bank returned a null payment response.");

        return new Payment
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
    }
}
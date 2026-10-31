using System.Globalization;
using System.Text.Json;

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

    public async Task<BankPaymentResponse> ProcessPaymentAsync(
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

        try
        {
            using var client = _httpClientFactory.CreateClient();
            using var response = await client.PostAsJsonAsync(_paymentsUrl, bankRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var bankResponse = await response.Content.ReadFromJsonAsync<BankPaymentResponse>(cancellationToken)
                ?? throw new BankUnavailableException("The bank returned a null payment response.");

            if (bankResponse.Authorized && string.IsNullOrWhiteSpace(bankResponse.AuthorizationCode))
            {
                throw new BankUnavailableException("The bank returned an authorization without an authorization code.");
            }

            return bankResponse;
        }
        catch (HttpRequestException exception)
        {
            throw new BankUnavailableException("The bank is unavailable.", exception);
        }
        catch (IOException exception)
        {
            throw new BankUnavailableException("Unable to read the bank response.", exception);
        }
        catch (JsonException exception)
        {
            throw new BankUnavailableException("The bank returned an invalid payment response.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BankUnavailableException("The bank request timed out.", exception);
        }
    }
}

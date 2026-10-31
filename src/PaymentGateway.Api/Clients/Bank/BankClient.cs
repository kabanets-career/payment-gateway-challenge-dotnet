using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;

using PaymentGateway.Api.Clients.Bank.Exceptions;
using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Clients.Bank;

public class BankClient : IBankClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _paymentsUrl;
    private readonly ILogger<BankClient> _logger;

    public BankClient(
        IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<BankClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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

        var stopwatch = Stopwatch.StartNew();
        HttpStatusCode? statusCode = null;
        try
        {
            using var client = _httpClientFactory.CreateClient();
            using var response = await client.PostAsJsonAsync(_paymentsUrl, bankRequest, cancellationToken);
            statusCode = response.StatusCode;
            response.EnsureSuccessStatusCode();

            var bankResponse = await response.Content.ReadFromJsonAsync<BankPaymentResponse>(cancellationToken)
                ?? throw new BankUnavailableException("The bank returned a null payment response.");

            if (bankResponse.Authorized && string.IsNullOrWhiteSpace(bankResponse.AuthorizationCode))
            {
                throw new BankUnavailableException("The bank returned an authorization without an authorization code.");
            }

            _logger.LogDebug(
                "Bank request completed: {StatusCode} in {DurationMs} ms",
                (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

            return bankResponse;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or BankUnavailableException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            statusCode ??= (exception as HttpRequestException)?.StatusCode;
            var category = exception switch
            {
                HttpRequestException when statusCode == HttpStatusCode.ServiceUnavailable => "Unavailable",
                HttpRequestException when statusCode.HasValue => "HttpError",
                HttpRequestException => "Network",
                OperationCanceledException => "Timeout",
                IOException => "ReadError",
                _ => "InvalidResponse"
            };
            var level = category is "Unavailable" or "Network" or "Timeout" ? LogLevel.Warning : LogLevel.Error;

            // Exception messages and bank response bodies can contain sensitive payment data.
            _logger.Log(level, "Bank request failed: {FailureCategory} {StatusCode} in {DurationMs} ms",
                category, (int?)statusCode, stopwatch.ElapsedMilliseconds);

            if (exception is BankUnavailableException)
            {
                throw;
            }

            var message = exception switch
            {
                OperationCanceledException => "The bank request timed out.",
                IOException => "Unable to read the bank response.",
                JsonException => "The bank returned an invalid payment response.",
                _ => "The bank is unavailable."
            };
            throw new BankUnavailableException(message, exception);
        }
    }
}

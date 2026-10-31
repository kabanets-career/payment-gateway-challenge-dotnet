using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Configuration;

using PaymentGateway.Api.Clients.Bank;
using PaymentGateway.Api.Clients.Bank.Exceptions;
using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Tests.Unit.Clients;

public class BankClientTests
{
    [Theory]
    [InlineData(4, "04/2030", 100)]
    [InlineData(11, "11/2030", 0)]
    public async Task PostsBankRequestToConfiguredUrl(int expiryMonth, string expiryDate, int amount)
    {
        const string paymentsUrl = "https://configured-bank.example/payments";
        var request = CreateRequest(expiryMonth, amount);
        using var handler = new StubHttpMessageHandler(async (message, _) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal(new Uri(paymentsUrl), message.RequestUri);
            Assert.Equal("application/json", message.Content!.Headers.ContentType!.MediaType);

            using var json = JsonDocument.Parse(await message.Content.ReadAsStringAsync());
            var body = json.RootElement;
            Assert.Equal(5, body.EnumerateObject().Count());
            Assert.Equal(request.CardNumber, body.GetProperty("card_number").GetString());
            Assert.Equal(expiryDate, body.GetProperty("expiry_date").GetString());
            Assert.Equal(request.Currency, body.GetProperty("currency").GetString());
            Assert.Equal(amount, body.GetProperty("amount").GetInt32());
            Assert.Equal(request.Cvv, body.GetProperty("cvv").GetString());

            return JsonResponse("""{"authorized":true,"authorization_code":"AUTH-123"}""");
        });
        var factory = new StubHttpClientFactory(handler);
        IBankClient client = new BankClient(factory, CreateConfiguration(paymentsUrl));

        await client.ProcessPaymentAsync(request);

        Assert.Equal(1, factory.CreateClientCount);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData(true, "0bb07405-6d44-4b50-a14f-7ae0beff13ad", PaymentStatus.Authorized)]
    [InlineData(false, "", PaymentStatus.Declined)]
    public async Task MapsBankResponseAndCommandToPayment(
        bool authorized, string authorizationCode, PaymentStatus expectedStatus)
    {
        var request = CreateRequest();
        var responseJson = JsonSerializer.Serialize(new
        {
            authorized,
            authorization_code = authorizationCode
        });
        using var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(responseJson)));
        IBankClient client = new BankClient(new StubHttpClientFactory(handler), CreateConfiguration());

        Payment payment = await client.ProcessPaymentAsync(request);

        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal(expectedStatus, payment.Status);
        Assert.Equal(authorizationCode, payment.AuthorizationCode);
        Assert.Equal("0042", payment.CardNumberLastFour);
        Assert.Equal(request.ExpiryMonth, payment.ExpiryMonth);
        Assert.Equal(request.ExpiryYear, payment.ExpiryYear);
        Assert.Equal(request.Currency, payment.Currency);
        Assert.Equal(request.Amount, payment.Amount);
    }

    [Theory]
    [InlineData("{}", "application/json")]
    [InlineData("Bank unavailable", "text/plain")]
    public async Task ThrowsBankUnavailableExceptionWhenBankReturns503(string body, string contentType)
    {
        using var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            }));
        var client = new BankClient(new StubHttpClientFactory(handler), CreateConfiguration());

        var exception = await Assert.ThrowsAsync<BankUnavailableException>(() =>
            client.ProcessPaymentAsync(CreateRequest()));

        Assert.Equal("The bank is unavailable.", exception.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ThrowsHttpRequestExceptionWithStatusCodeWhenBankReturnsError(HttpStatusCode statusCode)
    {
        using var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("Bank unavailable", Encoding.UTF8, "text/plain")
            }));
        var client = new BankClient(new StubHttpClientFactory(handler), CreateConfiguration());

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.ProcessPaymentAsync(CreateRequest()));

        Assert.Equal(statusCode, exception.StatusCode);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ThrowsWhenBankResponseIsNull()
    {
        using var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("null")));
        var client = new BankClient(new StubHttpClientFactory(handler), CreateConfiguration());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ProcessPaymentAsync(CreateRequest()));
    }

    [Fact]
    public async Task CancelsBankRequestWhenCallerCancels()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new StubHttpMessageHandler((_, cancellationToken) =>
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(JsonResponse("""{"authorized":true,"authorization_code":"AUTH-123"}"""));
        });
        var client = new BankClient(new StubHttpClientFactory(handler), CreateConfiguration());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ProcessPaymentAsync(CreateRequest(), cancellation.Token));
    }

    private static ProcessPaymentCommand CreateRequest(int expiryMonth = 4, int amount = 100) => new()
    {
        CardNumber = "2222405343240042",
        ExpiryMonth = expiryMonth,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = amount,
        Cvv = "123"
    };

    private static IConfiguration CreateConfiguration(string paymentsUrl = "http://localhost:8080/payments") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bank:PaymentsUrl"] = paymentsUrl
        }).Build();

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public int CreateClientCount { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreateClientCount++;
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return _send(request, cancellationToken);
        }
    }
}
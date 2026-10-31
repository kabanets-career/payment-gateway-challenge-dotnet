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
    [InlineData(11, "11/2030", 1)]
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
    [InlineData(true, "0bb07405-6d44-4b50-a14f-7ae0beff13ad")]
    [InlineData(false, "")]
    public async Task ReturnsDeserializedBankResponse(bool authorized, string authorizationCode)
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

        BankPaymentResponse response = await client.ProcessPaymentAsync(request);

        Assert.Equal(authorized, response.Authorized);
        Assert.Equal(authorizationCode, response.AuthorizationCode);
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
    public async Task WrapsHttpErrorsAsBankUnavailable(HttpStatusCode statusCode)
    {
        using var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("Bank unavailable", Encoding.UTF8, "text/plain")
            }));
        var client = new BankClient(new StubHttpClientFactory(handler), CreateConfiguration());

        var exception = await Assert.ThrowsAsync<BankUnavailableException>(() =>
            client.ProcessPaymentAsync(CreateRequest()));

        Assert.Equal(statusCode, Assert.IsType<HttpRequestException>(exception.InnerException).StatusCode);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"authorization_code\":\"AUTH-123\"}")]
    [InlineData("{\"authorized\":null}")]
    [InlineData("{\"authorized\":\"false\"}")]
    [InlineData("{\"authorized\":true}")]
    [InlineData("{\"authorized\":true,\"authorization_code\":null}")]
    [InlineData("{\"authorized\":true,\"authorization_code\":\"\"}")]
    [InlineData("{\"authorized\":true,\"authorization_code\":\"   \"}")]
    public async Task RejectsMalformedSuccessfulBankResponses(string body)
    {
        using var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(body)));
        var client = new BankClient(new StubHttpClientFactory(handler), CreateConfiguration());

        await Assert.ThrowsAsync<BankUnavailableException>(() =>
            client.ProcessPaymentAsync(CreateRequest()));
    }

    [Fact]
    public async Task AcceptsExplicitDeclineWithoutAuthorizationCode()
    {
        using var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("""{"authorized":false}""")));
        var client = new BankClient(new StubHttpClientFactory(handler), CreateConfiguration());

        var response = await client.ProcessPaymentAsync(CreateRequest());

        Assert.False(response.Authorized);
    }

    [Theory]
    [InlineData("connection")]
    [InlineData("read")]
    [InlineData("timeout")]
    public async Task WrapsCommunicationFailuresAndPreservesTheCause(string failureKind)
    {
        Exception failure = failureKind switch
        {
            "connection" => new HttpRequestException("Connection failed."),
            "read" => new IOException("Response stream failed."),
            _ => new TaskCanceledException("Bank timed out.", new TimeoutException())
        };
        using var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(failure));
        var client = new BankClient(new StubHttpClientFactory(handler), CreateConfiguration());
        using var cancellation = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<BankUnavailableException>(() =>
            client.ProcessPaymentAsync(CreateRequest(), cancellation.Token));

        Assert.Same(failure, exception.InnerException);
        Assert.False(cancellation.IsCancellationRequested);
        Assert.Equal(1, handler.RequestCount);
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

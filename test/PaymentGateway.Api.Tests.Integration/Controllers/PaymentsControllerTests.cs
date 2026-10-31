using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Repositories;

namespace PaymentGateway.Api.Tests.Integration.Controllers;

public class PaymentsControllerTests
{
    private static WebApplicationFactory<PaymentsController> CreateApplicationFactory() =>
        new WebApplicationFactory<PaymentsController>().WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.ClearProviders()));

    [Theory]
    [InlineData(PaymentStatus.Authorized, "Authorized")]
    [InlineData(PaymentStatus.Declined, "Declined")]
    public async Task RetrievesAPaymentSuccessfully(PaymentStatus status, string expectedStatus)
    {
        // Arrange
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Status = status,
            AuthorizationCode = "AUTH-123",
            ExpiryYear = 2030,
            ExpiryMonth = 9,
            Amount = 1050,
            CardNumberLastFour = "0042",
            Currency = "GBP"
        };

        using var factory = CreateApplicationFactory();
        var paymentsRepository = factory.Services.GetRequiredService<IPaymentsRepository>();
        paymentsRepository.Add(payment);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync($"/api/Payments/{payment.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paymentResponse = await response.Content.ReadFromJsonAsync<GetPaymentResponse>();
        Assert.NotNull(paymentResponse);
        using var responseBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedStatus, responseBody.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Returns404IfPaymentNotFound()
    {
        // Arrange
        using var factory = CreateApplicationFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync($"/api/Payments/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(true, PaymentStatus.Authorized, "Authorized")]
    [InlineData(false, PaymentStatus.Declined, "Declined")]
    public async Task ProcessesAndPersistsBankResult(
        bool authorized, PaymentStatus expectedStatus, string expectedStatusName)
    {
        using var bankHandler = new StubBankHandler(HttpStatusCode.OK, authorized);
        using var factory = CreateApplicationFactoryWithBank(bankHandler);
        using var client = factory.CreateClient();
        var request = CreateRequest();

        using var response = await client.PostAsJsonAsync("/api/Payments", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var paymentResponse = Assert.IsType<PostPaymentResponse>(
            await response.Content.ReadFromJsonAsync<PostPaymentResponse>());
        Assert.NotEqual(Guid.Empty, paymentResponse.Id);
        Assert.Equal(expectedStatus, paymentResponse.Status);
        Assert.Equal("0042", paymentResponse.CardNumberLastFour);
        Assert.Equal(request.ExpiryMonth, paymentResponse.ExpiryMonth);
        Assert.Equal(request.ExpiryYear, paymentResponse.ExpiryYear);
        Assert.Equal(request.Currency, paymentResponse.Currency);
        Assert.Equal(request.Amount, paymentResponse.Amount);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.EndsWith($"/api/Payments/{paymentResponse.Id}", location.ToString());

        var repository = factory.Services.GetRequiredService<IPaymentsRepository>();
        var storedPayment = Assert.IsType<Payment>(repository.Get(paymentResponse.Id));
        Assert.Equal(expectedStatus, storedPayment.Status);
        Assert.Equal(authorized ? "AUTH-123" : "", storedPayment.AuthorizationCode);

        using var retrieved = await client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, retrieved.StatusCode);
        Assert.Equal(GetPaymentResponse.ToResponse(storedPayment),
            await retrieved.Content.ReadFromJsonAsync<GetPaymentResponse>());

        Assert.Equal(1, bankHandler.RequestCount);
        using var bankRequest = JsonDocument.Parse(bankHandler.RequestBody!);
        Assert.Equal(request.CardNumber, bankRequest.RootElement.GetProperty("card_number").GetString());
        Assert.Equal("09/2030", bankRequest.RootElement.GetProperty("expiry_date").GetString());
        Assert.Equal(request.Currency, bankRequest.RootElement.GetProperty("currency").GetString());
        Assert.Equal(request.Amount, bankRequest.RootElement.GetProperty("amount").GetInt32());
        Assert.Equal(request.Cvv, bankRequest.RootElement.GetProperty("cvv").GetString());

        using var responseBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedStatusName, responseBody.RootElement.GetProperty("status").GetString());
        Assert.False(responseBody.RootElement.TryGetProperty("cardNumber", out _));
        Assert.False(responseBody.RootElement.TryGetProperty("cvv", out _));
        Assert.False(responseBody.RootElement.TryGetProperty("authorizationCode", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ReturnsBadGatewayWithoutPersistingWhenBankFails(HttpStatusCode bankStatus)
    {
        using var bankHandler = new StubBankHandler(bankStatus);
        var repository = new RecordingPaymentsRepository();
        using var factory = CreateApplicationFactoryWithBank(bankHandler).WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IPaymentsRepository>(repository)));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/Payments", CreateRequest());

        await AssertBadGateway(response);
        Assert.Empty(repository.AddedPayments);
        Assert.Equal(1, bankHandler.RequestCount);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"authorized\":null}")]
    [InlineData("{\"authorized\":true}")]
    [InlineData("{\"authorized\":true,\"authorization_code\":null}")]
    [InlineData("{\"authorized\":true,\"authorization_code\":\"   \"}")]
    public async Task ReturnsBadGatewayWithoutPersistingMalformedBankResponse(string body)
    {
        using var bankHandler = new StubBankHandler(HttpStatusCode.OK, responseBody: body);
        var repository = new RecordingPaymentsRepository();
        using var factory = CreateApplicationFactoryWithBank(bankHandler).WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IPaymentsRepository>(repository)));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/Payments", CreateRequest());

        await AssertBadGateway(response);
        Assert.Empty(repository.AddedPayments);
        Assert.Equal(1, bankHandler.RequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturnsBadGatewayWithoutPersistingForRealConnectionFailureOrTimeout(bool timeout)
    {
        var bankBuilder = WebApplication.CreateBuilder();
        bankBuilder.Logging.ClearProviders();
        bankBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var bank = bankBuilder.Build();
        var bankRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bank.MapPost("/payments", async (HttpContext context) =>
        {
            bankRequestReceived.SetResult();
            await Task.Delay(Timeout.Infinite, context.RequestAborted);
        });
        await bank.StartAsync();
        var paymentsUrl = bank.Urls.Single() + "/payments";
        if (!timeout)
        {
            await bank.StopAsync();
        }

        var repository = new RecordingPaymentsRepository();
        using var factory = CreateApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Bank:PaymentsUrl"] = paymentsUrl }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IPaymentsRepository>(repository);
                services.AddHttpClient(string.Empty, client => client.Timeout = TimeSpan.FromSeconds(1));
            });
        });
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/Payments", CreateRequest());

        await AssertBadGateway(response);
        Assert.Empty(repository.AddedPayments);
        if (timeout)
        {
            Assert.True(bankRequestReceived.Task.IsCompletedSuccessfully);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task AllowsNonPositiveAmountsAndPassesThemToBank(int amount)
    {
        using var bankHandler = new StubBankHandler(HttpStatusCode.OK);
        using var factory = CreateApplicationFactoryWithBank(bankHandler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/Payments", CreateRequest() with { Amount = amount });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var paymentResponse = Assert.IsType<PostPaymentResponse>(
            await response.Content.ReadFromJsonAsync<PostPaymentResponse>());
        Assert.Equal(amount, paymentResponse.Amount);
        Assert.Equal(PaymentStatus.Declined, paymentResponse.Status);
        Assert.Equal(1, bankHandler.RequestCount);
        using var bankRequest = JsonDocument.Parse(bankHandler.RequestBody!);
        Assert.Equal(amount, bankRequest.RootElement.GetProperty("amount").GetInt32());
    }

    [Fact]
    public async Task RejectsInvalidRequestWithoutCallingBank()
    {
        using var bankHandler = new StubBankHandler(HttpStatusCode.OK);
        using var factory = CreateApplicationFactoryWithBank(bankHandler);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/Payments", new PostPaymentRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(
            await response.Content.ReadFromJsonAsync<ValidationProblemDetails>());
        Assert.NotEmpty(problem.Errors);
        Assert.Equal(0, bankHandler.RequestCount);
    }

    private static PostPaymentRequest CreateRequest() => new()
    {
        CardNumber = "2222405343240042",
        ExpiryMonth = 9,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 1050,
        Cvv = "123"
    };

    private static async Task AssertBadGateway(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var problem = Assert.IsType<ProblemDetails>(await response.Content.ReadFromJsonAsync<ProblemDetails>());
        Assert.Equal(502, problem.Status);
        Assert.Equal("Payment processing unavailable", problem.Title);
        Assert.Equal("Unable to determine payment authorization outcome.", problem.Detail);
    }

    private sealed class RecordingPaymentsRepository : IPaymentsRepository
    {
        public List<Payment> AddedPayments { get; } = new();

        public void Add(Payment payment) => AddedPayments.Add(payment);

        public Payment? Get(Guid id) => AddedPayments.SingleOrDefault(payment => payment.Id == id);
    }

    private static WebApplicationFactory<PaymentsController> CreateApplicationFactoryWithBank(HttpMessageHandler handler) =>
        CreateApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(handler))));

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubBankHandler(
        HttpStatusCode statusCode, bool authorized = false, string? responseBody = null) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal(HttpMethod.Post, request.Method);
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = responseBody is not null
                    ? new StringContent(responseBody, Encoding.UTF8, "application/json")
                    : JsonContent.Create(new
                {
                    authorized,
                    authorization_code = authorized ? "AUTH-123" : ""
                })
            };
        }
    }
}

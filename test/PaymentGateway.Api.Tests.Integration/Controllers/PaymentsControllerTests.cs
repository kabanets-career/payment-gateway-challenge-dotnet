using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Repositories;

namespace PaymentGateway.Api.Tests.Integration.Controllers;

public class PaymentsControllerTests
{
    private static WebApplicationFactory<PaymentsController> CreateApplicationFactory() =>
        new WebApplicationFactory<PaymentsController>().WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.ClearProviders()));

    [Theory]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Declined)]
    [InlineData(PaymentStatus.Rejected)]
    public async Task RetrievesAPaymentSuccessfully(PaymentStatus status)
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
}

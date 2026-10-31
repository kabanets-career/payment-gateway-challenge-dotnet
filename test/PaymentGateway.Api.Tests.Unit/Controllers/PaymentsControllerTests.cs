using FluentValidation;
using FluentValidation.Results;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PaymentGateway.Api.Clients.Bank.Exceptions;
using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Tests.Unit.Logging;

namespace PaymentGateway.Api.Tests.Unit.Controllers;

public class PaymentsControllerTests
{
    [Fact]
    public async Task ReturnsValidationErrorsWhenValidationFails()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        using var serviceProvider = services.BuildServiceProvider();
        var request = new PostPaymentRequest();
        var validator = new StubValidator(new ValidationResult(new[]
        {
            new ValidationFailure(nameof(PostPaymentRequest.CardNumber), "Card number is invalid."),
            new ValidationFailure(nameof(PostPaymentRequest.CardNumber), "Another card number error."),
            new ValidationFailure(nameof(PostPaymentRequest.Currency), "Currency is unsupported.")
        }));
        var service = new StubPaymentService();
        var logger = new TestLogger<PaymentsController>();
        var controller = new PaymentsController(service, validator, logger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = serviceProvider }
            }
        };

        var result = await controller.PostPaymentAsync(request, CancellationToken.None);

        var badRequest = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal(2, problem.Errors.Count);
        Assert.Equal(new[] { "Card number is invalid.", "Another card number error." },
            problem.Errors[nameof(PostPaymentRequest.CardNumber)]);
        Assert.Equal(new[] { "Currency is unsupported." },
            problem.Errors[nameof(PostPaymentRequest.Currency)]);
        Assert.Same(request, validator.ValidatedRequest);
        Assert.Null(service.ReceivedCommand);
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Declined)]
    public async Task ReturnsCreatedPaymentWhenValidationSucceeds(PaymentStatus status)
    {
        var request = CreateRequest();
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Status = status,
            AuthorizationCode = "AUTH-123",
            CardNumberLastFour = "0042",
            ExpiryMonth = request.ExpiryMonth!.Value,
            ExpiryYear = request.ExpiryYear!.Value,
            Currency = request.Currency!,
            Amount = request.Amount!.Value
        };
        var service = new StubPaymentService(payment);
        var validator = new StubValidator(new ValidationResult());
        var controller = new PaymentsController(service, validator, NullLogger<PaymentsController>.Instance);
        using var cancellation = new CancellationTokenSource();

        var result = await controller.PostPaymentAsync(request, cancellation.Token);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(nameof(PaymentsController.GetPayment), created.ActionName);
        Assert.Equal(payment.Id, created.RouteValues!["id"]);
        Assert.Equal(PostPaymentResponse.ToResponse(payment), Assert.IsType<PostPaymentResponse>(created.Value));
        Assert.Same(request, validator.ValidatedRequest);
        Assert.Equal(request.ToCommand(), service.ReceivedCommand);
        Assert.Equal(cancellation.Token, service.CancellationToken);
    }

    [Fact]
    public async Task ReturnsBadGatewayProblemWhenBankIsUnavailable()
    {
        var service = new StubPaymentService { Failure = new BankUnavailableException("Internal bank details") };
        var logger = new TestLogger<PaymentsController>();
        var controller = new PaymentsController(service, new StubValidator(new ValidationResult()), logger);

        var result = await controller.PostPaymentAsync(CreateRequest(), CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, error.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(error.Value);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.Status);
        Assert.Equal("Payment processing unavailable", problem.Title);
        Assert.Equal("Unable to determine payment authorization outcome.", problem.Detail);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task ReturnsInternalServerErrorProblemWhenProcessingFailsUnexpectedly()
    {
        var service = new StubPaymentService { Failure = new InvalidOperationException("Sensitive internal details") };
        var logger = new TestLogger<PaymentsController>();
        var controller = new PaymentsController(service, new StubValidator(new ValidationResult()), logger);

        var result = await controller.PostPaymentAsync(CreateRequest(), CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, error.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(error.Value);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("Payment processing failed", problem.Title);
        Assert.Equal("An unexpected error occurred while processing the payment.", problem.Detail);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(service.Failure, entry.Exception);
        Assert.Equal(new[] { "{OriginalFormat}" }, entry.Properties.Keys);
        Assert.DoesNotContain("Sensitive internal details", entry.Message);
        Assert.DoesNotContain(CreateRequest().CardNumber!, entry.Message);
    }

    [Fact]
    public async Task PropagatesCancellationWhenCallerCancels()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new StubPaymentService { Failure = new OperationCanceledException(cancellation.Token) };
        var logger = new TestLogger<PaymentsController>();
        var controller = new PaymentsController(service, new StubValidator(new ValidationResult()), logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.PostPaymentAsync(CreateRequest(), cancellation.Token));

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void ReturnsOkWithPaymentWhenPaymentExists()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Status = PaymentStatus.Authorized,
            AuthorizationCode = "AUTH-123",
            CardNumberLastFour = "0042",
            ExpiryMonth = 9,
            ExpiryYear = 2030,
            Currency = "GBP",
            Amount = 1050
        };
        var service = new StubPaymentService(payment);
        var controller = new PaymentsController(service, new StubValidator(new ValidationResult()), NullLogger<PaymentsController>.Instance);

        var result = controller.GetPayment(payment.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        var response = Assert.IsType<GetPaymentResponse>(ok.Value);
        Assert.Equal(payment.Id, response.Id);
        Assert.Equal(payment.Status, response.Status);
        Assert.Equal(payment.CardNumberLastFour, response.CardNumberLastFour);
        Assert.Equal(payment.ExpiryMonth, response.ExpiryMonth);
        Assert.Equal(payment.ExpiryYear, response.ExpiryYear);
        Assert.Equal(payment.Currency, response.Currency);
        Assert.Equal(payment.Amount, response.Amount);
        Assert.Equal(payment.Id, service.RequestedId);
    }

    [Fact]
    public void ReturnsNotFoundWhenPaymentDoesNotExist()
    {
        var id = Guid.NewGuid();
        var service = new StubPaymentService();
        var controller = new PaymentsController(service, new StubValidator(new ValidationResult()), NullLogger<PaymentsController>.Instance);

        var result = controller.GetPayment(id);

        var notFound = Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
        Assert.Equal(id, service.RequestedId);
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

    private sealed class StubPaymentService : IPaymentService
    {
        private readonly Payment? _payment;

        public StubPaymentService(Payment? payment = null)
        {
            _payment = payment;
        }

        public Guid? RequestedId { get; private set; }
        public ProcessPaymentCommand? ReceivedCommand { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public Exception? Failure { get; init; }

        public Task<Payment> ProcessAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
        {
            ReceivedCommand = command;
            CancellationToken = cancellationToken;
            return Failure is not null ? Task.FromException<Payment>(Failure) : Task.FromResult(_payment!);
        }

        public Payment? Get(Guid id)
        {
            RequestedId = id;
            return _payment;
        }
    }

    private sealed class StubValidator : AbstractValidator<PostPaymentRequest>
    {
        private readonly ValidationResult _result;

        public StubValidator(ValidationResult result)
        {
            _result = result;
        }

        public PostPaymentRequest? ValidatedRequest { get; private set; }

        public override Task<ValidationResult> ValidateAsync(
            ValidationContext<PostPaymentRequest> context,
            CancellationToken cancellation = default)
        {
            ValidatedRequest = context.InstanceToValidate;
            return Task.FromResult(_result);
        }
    }
}

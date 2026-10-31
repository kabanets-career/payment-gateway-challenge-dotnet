using FluentValidation;
using FluentValidation.Results;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;

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
        var controller = new PaymentsController(new PaymentsRepository(), validator)
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
    }

    [Fact]
    public async Task ReturnsNoContentWhenValidationSucceeds()
    {
        var request = new PostPaymentRequest();
        var validator = new StubValidator(new ValidationResult());
        var controller = new PaymentsController(new PaymentsRepository(), validator);

        var result = await controller.PostPaymentAsync(request, CancellationToken.None);

        var noContent = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContent.StatusCode);
        Assert.Same(request, validator.ValidatedRequest);
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

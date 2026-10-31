using Microsoft.AspNetCore.Mvc;

using FluentValidation;

using PaymentGateway.Api.Clients.Bank.Exceptions;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : Controller
{
    private readonly IPaymentService _paymentService;
    private readonly IValidator<PostPaymentRequest> _postPaymentRequestValidator;

    public PaymentsController(
        IPaymentService paymentService,
        IValidator<PostPaymentRequest> postPaymentRequestValidator)
    {
        _paymentService = paymentService;
        _postPaymentRequestValidator = postPaymentRequestValidator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(PostPaymentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PostPaymentAsync(
        [FromBody] PostPaymentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var validationResult = await _postPaymentRequestValidator.ValidateAsync(request, cancellationToken);

            if (!validationResult.IsValid)
            {
                foreach (var error in validationResult.Errors)
                {
                    ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
                }

                return ValidationProblem(ModelState);
            }

            var payment = await _paymentService.ProcessAsync(request.ToCommand(), cancellationToken);
            return CreatedAtAction(nameof(GetPayment), new { id = payment.Id }, PostPaymentResponse.ToResponse(payment));
        }
        catch (BankUnavailableException)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Payment processing unavailable",
                detail: "Unable to determine payment authorization outcome.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Payment processing failed",
                detail: "An unexpected error occurred while processing the payment.");
        }
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GetPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<GetPaymentResponse> GetPayment(Guid id)
    {
        var payment = _paymentService.Get(id);

        return payment is null ? NotFound() : Ok(GetPaymentResponse.ToResponse(payment));
    }
}
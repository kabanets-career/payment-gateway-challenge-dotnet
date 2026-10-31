using FluentValidation;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Providers;

namespace PaymentGateway.Api.Validators;

public class PostPaymentRequestValidator : AbstractValidator<PostPaymentRequest>
{
    public PostPaymentRequestValidator(TimeProvider timeProvider, ICurrencyProvider currencyProvider)
    {
        RuleFor(request => request.CardNumber)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Length(14, 19)
            .Matches("\\A[0-9]+\\z")
            .WithMessage("Card number must contain only numeric characters.");

        RuleFor(request => request.ExpiryMonth)
            .InclusiveBetween(1, 12);

        RuleFor(request => request.ExpiryYear)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must((request, year) =>
            {
                var now = timeProvider.GetUtcNow();
                return year > now.Year || (year == now.Year && request.ExpiryMonth > now.Month);
            })
            .WithMessage("The combination of expiry month and year must be in the future.");

        RuleFor(request => request.Currency)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Length(3)
            .Must(currency => currencyProvider.SupportedCurrencies.Contains(currency, StringComparer.Ordinal))
            .WithMessage(_ => $"Currency must be one of {string.Join(", ", currencyProvider.SupportedCurrencies)}.");

        // The integer type enforces whole minor units; null distinguishes an omitted amount from zero.
        RuleFor(request => request.Amount)
            .NotNull();

        RuleFor(request => request.Cvv)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Length(3, 4)
            .Matches("\\A[0-9]+\\z")
            .WithMessage("CVV must contain only numeric characters.");
    }
}

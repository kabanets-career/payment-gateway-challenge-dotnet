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
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Expiry month is required.")
            .InclusiveBetween(1, 12);

        RuleFor(request => request.ExpiryYear)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Expiry year is required.")
            .InclusiveBetween(1, 9999);

        RuleFor(request => request.ExpiryYear)
            .Must((request, year) =>
            {
                var now = timeProvider.GetUtcNow();
                return year > now.Year || (year == now.Year && request.ExpiryMonth > now.Month);
            })
            .WithMessage("The combination of expiry month and year must be in the future.")
            .When(request => request.ExpiryMonth is >= 1 and <= 12
                && request.ExpiryYear is >= 1 and <= 9999);

        RuleFor(request => request.Currency)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Length(3)
            .Must(currency => currencyProvider.SupportedCurrencies.Contains(currency, StringComparer.Ordinal))
            .WithMessage(_ => $"Currency must be one of {string.Join(", ", currencyProvider.SupportedCurrencies)}.");

        // The integer type enforces whole minor units; null distinguishes an omitted amount from zero.
        RuleFor(request => request.Amount)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Amount is required.")
            .GreaterThan(0).WithMessage("Amount must be greater than zero.");

        RuleFor(request => request.Cvv)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Length(3, 4)
            .Matches("\\A[0-9]+\\z")
            .WithMessage("CVV must contain only numeric characters.");
    }
}

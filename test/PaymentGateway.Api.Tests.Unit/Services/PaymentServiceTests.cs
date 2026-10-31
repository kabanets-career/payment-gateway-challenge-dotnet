using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PaymentGateway.Api.Clients.Bank;
using PaymentGateway.Api.Clients.Bank.Exceptions;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Repositories;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Tests.Unit.Logging;

namespace PaymentGateway.Api.Tests.Unit.Services;

public class PaymentServiceTests
{
    [Theory]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Declined)]
    public void ReturnsPaymentFromRepository(PaymentStatus status)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Status = status,
            AuthorizationCode = "AUTH-123",
            CardNumberLastFour = "0042",
            ExpiryMonth = 9,
            ExpiryYear = 2030,
            Currency = "GBP",
            Amount = 1050
        };
        var repository = new StubPaymentsRepository(payment);
        var service = new PaymentService(repository, new StubBankClient(), NullLogger<PaymentService>.Instance);

        var result = service.Get(payment.Id);

        Assert.Equal(payment.Id, repository.RequestedId);
        Assert.Same(payment, result);
    }

    [Fact]
    public void ReturnsNullWhenRepositoryCannotFindPayment()
    {
        var id = Guid.NewGuid();
        var repository = new StubPaymentsRepository();
        var service = new PaymentService(repository, new StubBankClient(), NullLogger<PaymentService>.Instance);

        var result = service.Get(id);

        Assert.Null(result);
        Assert.Equal(id, repository.RequestedId);
    }

    [Theory]
    [InlineData(true, "AUTH-123", PaymentStatus.Authorized)]
    [InlineData(false, "", PaymentStatus.Declined)]
    public async Task MapsBankResponseAndCommandToPaymentThenPersists(
        bool authorized, string authorizationCode, PaymentStatus expectedStatus)
    {
        var command = CreateCommand();
        var bankResponse = new BankPaymentResponse
        {
            Authorized = authorized,
            AuthorizationCode = authorizationCode
        };
        var repository = new StubPaymentsRepository();
        var logger = new TestLogger<PaymentService>();
        using var cancellation = new CancellationTokenSource();
        var bankClient = new StubBankClient((receivedCommand, token) =>
        {
            Assert.Same(command, receivedCommand);
            Assert.Equal(cancellation.Token, token);
            Assert.Empty(repository.AddedPayments);
            return Task.FromResult(bankResponse);
        });
        IPaymentService service = new PaymentService(repository, bankClient, logger);

        var result = await service.ProcessAsync(command, cancellation.Token);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(authorizationCode, result.AuthorizationCode);
        Assert.Equal("0042", result.CardNumberLastFour);
        Assert.Equal(command.ExpiryMonth, result.ExpiryMonth);
        Assert.Equal(command.ExpiryYear, result.ExpiryYear);
        Assert.Equal(command.Currency, result.Currency);
        Assert.Equal(command.Amount, result.Amount);
        Assert.Same(result, Assert.Single(repository.AddedPayments));
        Assert.Equal(1, bankClient.RequestCount);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal(result.Id, entry.Properties["PaymentId"]);
        Assert.Equal(expectedStatus, entry.Properties["PaymentStatus"]);
        Assert.Equal(command.Currency, entry.Properties["Currency"]);
        Assert.DoesNotContain(command.CardNumber, entry.Message);
        Assert.DoesNotContain(authorizationCode.Length > 0 ? authorizationCode : "AUTH-123", entry.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DoesNotPersistWhenBankFails(bool unavailable)
    {
        Exception failure = unavailable
            ? new BankUnavailableException("The bank is unavailable.")
            : new InvalidOperationException("Unexpected bank failure.");
        var bankClient = new StubBankClient((_, _) => Task.FromException<BankPaymentResponse>(failure));
        var repository = new StubPaymentsRepository();
        var logger = new TestLogger<PaymentService>();
        var service = new PaymentService(repository, bankClient, logger);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => service.ProcessAsync(CreateCommand()));

        Assert.Same(failure, exception);
        Assert.Empty(repository.AddedPayments);
        Assert.Equal(1, bankClient.RequestCount);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task DoesNotPersistWhenBankRequestIsCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var bankClient = new StubBankClient((_, token) => Task.FromCanceled<BankPaymentResponse>(token));
        var repository = new StubPaymentsRepository();
        var service = new PaymentService(repository, bankClient, NullLogger<PaymentService>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ProcessAsync(CreateCommand(), cancellation.Token));

        Assert.Empty(repository.AddedPayments);
    }

    [Fact]
    public async Task PropagatesRepositoryFailureAfterBankResponds()
    {
        var bankResponse = new BankPaymentResponse { Authorized = true, AuthorizationCode = "AUTH-123" };
        var bankClient = new StubBankClient((_, _) => Task.FromResult(bankResponse));
        var failure = new InvalidOperationException("Unable to persist payment.");
        var repository = new StubPaymentsRepository { AddFailure = failure };
        var logger = new TestLogger<PaymentService>();
        var service = new PaymentService(repository, bankClient, logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessAsync(CreateCommand()));

        Assert.Same(failure, exception);
        Assert.Equal(1, bankClient.RequestCount);
        Assert.Empty(logger.Entries);
    }

    private static ProcessPaymentCommand CreateCommand() => new()
    {
        CardNumber = "2222405343240042",
        ExpiryMonth = 9,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 1050,
        Cvv = "123"
    };

    private sealed class StubBankClient : IBankClient
    {
        private readonly Func<ProcessPaymentCommand, CancellationToken, Task<BankPaymentResponse>>? _process;

        public StubBankClient(Func<ProcessPaymentCommand, CancellationToken, Task<BankPaymentResponse>>? process = null)
        {
            _process = process;
        }

        public int RequestCount { get; private set; }

        public Task<BankPaymentResponse> ProcessPaymentAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return _process?.Invoke(command, cancellationToken) ?? throw new NotSupportedException();
        }
    }

    private sealed class StubPaymentsRepository : IPaymentsRepository
    {
        private readonly Payment? _payment;

        public StubPaymentsRepository(Payment? payment = null)
        {
            _payment = payment;
        }

        public Guid? RequestedId { get; private set; }
        public List<Payment> AddedPayments { get; } = new();
        public Exception? AddFailure { get; init; }

        public void Add(Payment payment)
        {
            if (AddFailure is not null)
            {
                throw AddFailure;
            }

            AddedPayments.Add(payment);
        }

        public Payment? Get(Guid id)
        {
            RequestedId = id;
            return _payment;
        }
    }
}

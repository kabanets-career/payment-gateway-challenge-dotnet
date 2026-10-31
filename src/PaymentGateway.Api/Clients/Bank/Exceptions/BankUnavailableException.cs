namespace PaymentGateway.Api.Clients.Bank.Exceptions;

public sealed class BankUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

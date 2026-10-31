using System.Text.Json.Serialization;

namespace PaymentGateway.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PaymentStatus>))]
public enum PaymentStatus
{
    Authorized,
    Declined,
    Rejected
}
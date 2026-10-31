# Payment Gateway

A minimal ASP.NET Core 8 API for validating and processing card payments through the supplied acquiring-bank simulator, and retrieving previous payment results.

## Getting started

**Requirements:** .NET 8 SDK and Docker with Compose.

```bash
docker compose up -d
dotnet run --project src/PaymentGateway.Api --launch-profile PaymentGateway.Api
```

Swagger is available at `https://localhost:7092/swagger` (development profile). The bank simulator runs at `http://localhost:8080`.

## API

**POST `/api/Payments`** — validate and process a payment:

```json
{
  "cardNumber": "2222405343248877",
  "expiryMonth": 12,
  "expiryYear": 2030,
  "currency": "GBP",
  "amount": 1050,
  "cvv": "123"
}
```

- **201 Created:** Bank returned an authorization decision (`Authorized` or `Declined`), with a generated payment ID and safe card details.
- **400 Bad Request:** Rejected by validation; no bank call and no payment stored.
- **502 Bad Gateway:** Bank communication or response failure; authorization outcome is unknown and no payment is stored.

Payment outcomes are **Authorized** (bank approved; persisted), **Declined** (bank refused authorization; persisted), and **Rejected** (HTTP 400 validation failure; never persisted). A rejected request returns validation errors rather than a payment result.

**GET `/api/Payments/{id}`** — return the stored payment (**200 OK**) or **404 Not Found**.

The simulator authorizes cards ending in an odd digit, declines cards ending in an even nonzero digit, and returns 503 for cards ending in zero.

## Tests

```bash
dotnet restore
dotnet build PaymentGateway.sln --configuration Release
dotnet test PaymentGateway.sln --configuration Release
```

Unit tests cover validation, payment processing, safe logging and concurrency. HTTP integration tests cover API responses, persistence boundaries, malformed bank responses, and real connection failures and timeouts.

## Design decisions and assumptions

- **Separation of responsibilities:** Controller maps API requests, the service orchestrates processing and persistence, and `BankClient` handles the bank protocol.
- **Validation assumptions:** Amount must be a positive integer in minor currency units (the brief leaves the permitted range undefined). Both expiry fields are required; month must be 1–12 and year 1–9999 to fit the bank's four-digit `MM/YYYY` format. Their combination must be strictly in the future; the current month is rejected, using UTC. Supported currencies are `GBP`, `USD`, and `EUR`.
- **Storage and security:** A thread-safe in-memory repository stores payment results, including only the last four card digits—not the full card number or CVV. Data is lost on restart.
- **Failure handling:** HTTP errors (including 503), network failures, timeouts and invalid or incomplete bank responses are not declines. Unknown authorization outcomes are never persisted as successful bank decisions. No automatic retries are performed. Caller cancellation propagates without being logged as a bank failure. Durable persistence, idempotency, and reconciliation would be needed in production.
- **Bank configuration:** `Bank:PaymentsUrl` and `Bank:TimeoutSeconds` are validated at startup. The timeout defaults to 10 seconds in `appsettings.json`; override it with configuration or the `Bank__TimeoutSeconds` environment variable using a positive value.
- **Observability:** Structured logging records payment outcomes and bank failures with trace correlation. Failure logs include category, HTTP status when available, and elapsed milliseconds; unavailability uses Warning and unexpected HTTP/protocol failures use Error. Logs exclude full card numbers, CVVs, expiry dates, authorization codes, bodies and bank exception text. Responses expose only the last four card digits alongside the required payment details.

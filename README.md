# Instructions for candidates

This is the .NET version of the Payment Gateway challenge. If you haven't already read this [README.md](https://github.com/cko-recruitment/) on the details of this exercise, please do so now.

## Template structure
```
src/
    PaymentGateway.Api - a skeleton ASP.NET Core Web API
test/
    PaymentGateway.Api.Tests.Unit - provider and request validator unit tests
    PaymentGateway.Api.Tests.Integration - HTTP endpoint tests using WebApplicationFactory
imposters/ - contains the bank simulator configuration. Don't change this

.editorconfig - don't change this. It ensures a consistent set of rules for submissions when reformatting code
docker-compose.yml - configures the bank simulator
PaymentGateway.sln
```

Feel free to change the structure of the solution, use a different test library etc.

## Running tests

Run all tests:

```sh
dotnet test PaymentGateway.sln
```

Run unit tests or integration tests independently:

```sh
dotnet test test/PaymentGateway.Api.Tests.Unit/PaymentGateway.Api.Tests.Unit.csproj
dotnet test test/PaymentGateway.Api.Tests.Integration/PaymentGateway.Api.Tests.Integration.csproj
```

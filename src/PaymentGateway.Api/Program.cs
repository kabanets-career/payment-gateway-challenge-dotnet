using FluentValidation;

using PaymentGateway.Api.Clients.Bank;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Providers;
using PaymentGateway.Api.Repositories;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Validators;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);
builder.Logging.Configure(options =>
    options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient(string.Empty, (services, client) =>
    client.Timeout = TimeSpan.FromSeconds(
        services.GetRequiredService<IConfiguration>().GetValue<double>("Bank:TimeoutSeconds")));
builder.Services.AddScoped<IBankClient, BankClient>();
builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<ICurrencyProvider, CurrencyProvider>();
builder.Services.AddScoped<IValidator<PostPaymentRequest>, PostPaymentRequestValidator>();

var app = builder.Build();

var paymentsUrl = app.Configuration["Bank:PaymentsUrl"];
if (!Uri.TryCreate(paymentsUrl, UriKind.Absolute, out var bankUri)
    || (bankUri.Scheme != Uri.UriSchemeHttp && bankUri.Scheme != Uri.UriSchemeHttps))
{
    throw new InvalidOperationException("Bank:PaymentsUrl must be an absolute HTTP or HTTPS URL.");
}

var timeoutSeconds = app.Configuration.GetValue<double?>("Bank:TimeoutSeconds");
if (timeoutSeconds is null || !double.IsFinite(timeoutSeconds.Value)
    || timeoutSeconds <= 0 || timeoutSeconds > int.MaxValue / 1000d)
{
    throw new InvalidOperationException("Bank:TimeoutSeconds must be positive and no greater than 2147483.647 seconds.");
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

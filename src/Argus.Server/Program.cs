using System.Text.Json.Serialization;
using Argus.Server.Data;
using Argus.Server.Features.Account;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Health;
using Argus.Server.Features.Info;
using Argus.Server.Features.Users;
using Argus.Server.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddArgusOptions();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddArgusDatabase();
builder.Services.AddDataProtection()
    .SetApplicationName("Argus")
    .PersistKeysToDbContext<ArgusDbContext>();
builder.Services.AddArgusAuth();
builder.Services.AddArgusRateLimiting();

builder.Services.AddArgusHealthChecks()
    .AddDbContextCheck<ArgusDbContext>("database", tags: [HealthEndpoints.ReadyTag]);
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSecurityHeaders();
app.UseCsrfProtection();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.MapArgusHealthChecks();

var api = app.MapGroup("/api");
api.MapInfoEndpoints();
api.MapAuthEndpoints();
api.MapAccountEndpoints();
api.MapUserEndpoints();

await app.InitializeDatabaseAsync();

await app.RunAsync();

public partial class Program;

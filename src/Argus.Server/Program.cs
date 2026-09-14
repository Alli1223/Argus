using System.Text.Json.Serialization;
using Argus.Server.Features.Health;
using Argus.Server.Features.Info;
using Argus.Server.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddArgusOptions(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddArgusHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapArgusHealthChecks();

var api = app.MapGroup("/api");
api.MapInfoEndpoints();

app.Run();

public partial class Program;

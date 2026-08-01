using Game.Api.Hubs;
using Game.Api.Middleware;
using Game.Application;
using Game.Infrastructure;
using Game.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var frontendOrigins = builder.Configuration
    .GetSection("Cors:Origins")
    .Get<string[]>() ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy
            .WithOrigins(frontendOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var signalR = builder.Services
    .AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
var redisConnection = builder.Configuration.GetConnectionString("Redis");
var useRedisBackplane = builder.Configuration.GetValue<bool>("Redis:UseSignalRBackplane");

if (useRedisBackplane && !string.IsNullOrWhiteSpace(redisConnection))
{
    signalR.AddStackExchangeRedis(redisConnection);
}

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:InitializeOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.UseCors("frontend");
app.UseExceptionHandler();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<GameHub>("/hubs/game");

app.MapGet("/api/system/status", () => Results.Ok(new
{
    service = "game-api",
    status = "ready",
    database = "postgresql",
    realtime = "signalr",
    cache = string.IsNullOrWhiteSpace(redisConnection) ? "disabled" : "redis",
    backplane = useRedisBackplane ? "redis" : "single-node"
}));

app.Run();

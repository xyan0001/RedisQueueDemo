using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TerminalManagementService.BackgroundServices;
using TerminalManagementService.Models;
using TerminalManagementService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure terminal settings
builder.Services.Configure<TerminalConfiguration>(
    builder.Configuration.GetSection("TerminalConfiguration"));

// Configure Redis
builder.Services.AddSingleton<ConnectionMultiplexer>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
});

// Register services
builder.Services.AddSingleton<ITerminalService, RedisTerminalService>();
builder.Services.AddHostedService<TerminalCleanupService>();

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

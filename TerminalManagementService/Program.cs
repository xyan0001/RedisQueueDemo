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
    var options = ConfigurationOptions.Parse(connectionString);
    options.AbortOnConnectFail = false; // Don't throw if Redis is unavailable
    return ConnectionMultiplexer.Connect(options);
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

// Initialize terminals and preload cache
using (var scope = app.Services.CreateScope())
{
    var terminalService = scope.ServiceProvider.GetRequiredService<ITerminalService>();
    try
    {
        await terminalService.InitializeTerminalsAsync();
        
        // Preload terminal cache if service supports it
        if (terminalService is RedisTerminalService redisTerminalService)
        {
            await redisTerminalService.PreloadTerminalCacheAsync();
            
            var metrics = redisTerminalService.GetCacheMetrics();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Terminal cache initialized with {Count} terminals", metrics.hits + metrics.misses);
        }
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error initializing terminals or preloading cache");
    }
}

app.Run();

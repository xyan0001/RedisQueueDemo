using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TerminalManagementService;
using TerminalManagementService.Models;
using TerminalManagementService.Services;

var builder = WebApplication.CreateBuilder(args);

// Check command line arguments for Redis initialization
bool initializeRedisOnly = args.Contains("--initialize-redis");

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure terminal settings
builder.Services.Configure<TerminalConfiguration>(
    builder.Configuration.GetSection("TerminalConfiguration"));

// Get Redis connection string from configuration
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");

// Register blocking Redis connection (for blocking operations like BLPOP)
builder.Services.AddSingleton<BlockingRedisConnection>(sp =>
{
    var options = ConfigurationOptions.Parse(redisConnectionString ?? "localhost:6379");
    options.ConnectRetry = 5;
    options.ConnectTimeout = 5000;
    options.ClientName = "BlockingRedisClient";
    return new BlockingRedisConnection(ConnectionMultiplexer.Connect(options));
});

// Register non-blocking Redis connection (for non-blocking operations like RPUSH, status updates, etc.)
builder.Services.AddSingleton<NonBlockingRedisConnection>(sp =>
{
    var options = ConfigurationOptions.Parse(redisConnectionString ?? "localhost:6379");
    options.ConnectRetry = 5;
    options.ConnectTimeout = 5000;
    options.ClientName = "NonBlockingRedisClient";
    return new NonBlockingRedisConnection(ConnectionMultiplexer.Connect(options));
});

// Register services with explicit injection for both Redis instances
builder.Services.AddSingleton<ITerminalService>(sp =>
{
    var blockingRedis = sp.GetRequiredService<BlockingRedisConnection>();
    var nonBlockingRedis = sp.GetRequiredService<NonBlockingRedisConnection>();
    var configOptions = sp.GetRequiredService<IOptions<TerminalConfiguration>>();
    var logger = sp.GetRequiredService<ILogger<RedisTerminalService>>();
    var appConfig = sp.GetRequiredService<IConfiguration>();
    return new RedisTerminalService(blockingRedis, nonBlockingRedis, configOptions, appConfig, logger);
});
builder.Services.AddTransient<TerminalLifecycleSimulator>();
//builder.Services.AddHostedService<TerminalCleanupService>();

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// If running in Redis initialization mode, just initialize Redis and exit
//if (initializeRedisOnly)
//{
//    using (var scope = app.Services.CreateScope())
//    {
//        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
//        logger.LogInformation("Running in Redis initialization mode");

//        try
//        {
//            var terminalService = scope.ServiceProvider.GetRequiredService<ITerminalService>();

//            // Force initialization regardless of configuration setting
//            logger.LogInformation("Initializing Redis terminals...");

//            await terminalService.InitializeTerminalsAsync();
//            logger.LogInformation("Redis initialization completed successfully");
//        }
//        catch (Exception ex)
//        {
//            logger.LogError(ex, "Error initializing Redis");
//            Environment.ExitCode = 1;
//        }

//        return; // Exit the application after initialization
//    }
//}


using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Starting Redis initialization check...");

    try
    {
        var terminalService = scope.ServiceProvider.GetRequiredService<ITerminalService>();

        // If Redis already initialized, skip this step
        if (!await terminalService.IsInitialized())
        {
            logger.LogInformation("Initializing Redis terminals...");
            await terminalService.InitializeTerminalsAsync();
            logger.LogInformation("Redis initialization completed successfully");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error initializing Redis");
        //Environment.ExitCode = 1;
    }
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
app.MapHealthChecks("/health");

app.Run();

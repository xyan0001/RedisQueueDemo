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

// Register default Redis connection
builder.Services.AddSingleton<DefaultRedisConnection>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    var options = ConfigurationOptions.Parse(connectionString);
    options.AbortOnConnectFail = false;
    return new DefaultRedisConnection(ConnectionMultiplexer.Connect(options));
});

// Register release Redis connection
builder.Services.AddSingleton<ReleaseRedisConnection>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("RedisRelease") ?? builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    var options = ConfigurationOptions.Parse(connectionString);
    options.AbortOnConnectFail = false;
    return new ReleaseRedisConnection(ConnectionMultiplexer.Connect(options));
});

// Register services with explicit injection for both Redis instances
builder.Services.AddSingleton<ITerminalService>(sp =>
{
    var defaultRedis = sp.GetRequiredService<DefaultRedisConnection>();
    var releaseRedis = sp.GetRequiredService<ReleaseRedisConnection>();
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TerminalConfiguration>>();
    var appConfig = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<RedisTerminalService>>();
    return new RedisTerminalService(defaultRedis.Connection, releaseRedis.Connection, config, appConfig, logger);
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

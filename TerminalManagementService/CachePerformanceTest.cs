using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Diagnostics;
using TerminalManagementService.Models;
using TerminalManagementService.Services;

namespace TerminalManagementService;

public class CachePerformanceTest
{
    private readonly ITerminalService _terminalService;
    private readonly ILogger _logger;

    /// <summary>
    /// Constructor for use with dependency injection
    /// </summary>
    public CachePerformanceTest(ITerminalService terminalService, ILogger logger)
    {
        _terminalService = terminalService ?? throw new ArgumentNullException(nameof(terminalService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Run tests with an existing terminal service
    /// </summary>
    public async Task RunTestsAsync()
    {
        _logger.LogInformation("Starting cache performance test with existing terminal service");

        try
        {
            // Test 1: Repeated allocation/release with cache
            _logger.LogInformation("Running allocation/release test (1000 operations)...");
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < 1000; i++)
            {
                // Allocate terminal
                var terminal = await _terminalService.AllocateTerminalAsync();
                if (terminal != null)
                {
                    // Release terminal
                    await _terminalService.ReleaseTerminalAsync(terminal.Id);
                }
            }

            stopwatch.Stop();
            _logger.LogInformation("Completed 1000 allocation/release cycles in {ElapsedMs}ms",
                stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Average time per cycle: {AvgTimeMs}ms", stopwatch.ElapsedMilliseconds / 1000.0);

            // Get final cache metrics
            var metrics = _terminalService.GetCacheMetrics();
            _logger.LogInformation("Final cache metrics: Hits={Hits}, Misses={Misses}, HitRate={HitRate}%",
                metrics.hits, metrics.misses, metrics.hitRate.ToString("0.00"));

            // Estimate performance improvement
            if (metrics.hits > 0 && metrics.misses > 0)
            {
                // Assuming cache hits are 10x faster than cache misses
                double avgMissTimeMs = 2.0; // Estimated time for Redis lookup
                double avgHitTimeMs = 0.2; // Estimated time for cache lookup

                double estimatedTimeWithoutCache = 1000 * avgMissTimeMs;
                double estimatedTimeWithCache = metrics.hits * avgHitTimeMs + metrics.misses * avgMissTimeMs;
                double speedupFactor = estimatedTimeWithoutCache / estimatedTimeWithCache;

                _logger.LogInformation("Estimated performance improvement: {SpeedupFactor}x faster with caching",
                    speedupFactor.ToString("0.0"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache performance test");
            throw;
        }
    }

    /// <summary>
    /// Run performance tests for the terminal caching implementation
    /// </summary>
    public static async Task RunTests()
    {
        Console.WriteLine("Terminal Info Cache Performance Test");
        Console.WriteLine("====================================");        // Create simple configuration
        var config = new TerminalConfiguration
        {
            InitialTerminalCount = 40,
            Scheme = "http",
            UsernamePattern = "user{0}",
            PasswordPattern = "pass{0}",
            TerminalIdPrefix = "terminal-",
            SessionTimeoutSeconds = 300,
            OrphanedTerminalTimeoutSeconds = 30,
            PodName = "local-pod",
            Secret = "<terminals_password>"
        };

        // Create logger
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<RedisTerminalService>();

        // Create options
        var options = Options.Create(config);

        try
        {            // Create Redis connection with failsafe configuration
            var redisOptions = ConfigurationOptions.Parse("localhost:6379");
            redisOptions.AbortOnConnectFail = false;
            using var redis = ConnectionMultiplexer.Connect(redisOptions);

            // Create configuration
            var configurationBuilder = new ConfigurationBuilder();
            var configuration = configurationBuilder.Build();

            // Create terminal service
            var terminalService = new RedisTerminalService(redis, options, configuration, logger);

            // Initialize terminals
            Console.WriteLine("\nInitializing terminals...");
            await terminalService.InitializeTerminalsAsync();

            // Preload terminal cache
            Console.WriteLine("\nPreloading terminal cache...");
            await terminalService.PreloadTerminalCacheAsync();

            // Run tests with the created terminal service
            var test = new CachePerformanceTest(terminalService, logger);
            await test.RunTestsAsync();

            // Get final cache metrics
            var metrics = terminalService.GetCacheMetrics();
            Console.WriteLine($"\nFinal cache metrics:");
            Console.WriteLine($"Cache hits: {metrics.hits}");
            Console.WriteLine($"Cache misses: {metrics.misses}");
            Console.WriteLine($"Hit rate: {metrics.hitRate:0.00}%");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}

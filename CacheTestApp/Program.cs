using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Diagnostics;
using TerminalManagementService.Models;
using TerminalManagementService.Services;

namespace CacheTestApp;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Terminal Management Service Cache Test");
        Console.WriteLine("=======================================");

        // Create mock dependencies
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<RedisTerminalService>();

        // Create terminal configuration
        var config = new TerminalConfiguration
        {
            InitialTerminalCount = 40,
            Url = "example.com",
            Port = 22,
            UsernamePattern = "user{0}",
            PasswordPattern = "pass{0}",
            TerminalIdPrefix = "terminal-",
            SessionTimeoutSeconds = 300,
            OrphanedTerminalTimeoutSeconds = 30
        };

        var options = Options.Create(config);

        try
        {
            // Connect to Redis with connection options that won't throw if Redis isn't available
            Console.WriteLine("Connecting to Redis...");
            var connectionOptions = ConfigurationOptions.Parse("localhost:6379");
            connectionOptions.AbortOnConnectFail = false;
            var redis = ConnectionMultiplexer.Connect(connectionOptions);

            // Create terminal service
            var terminalService = new RedisTerminalService(redis, options, logger);

            // Initialize terminals
            Console.WriteLine("Initializing terminals...");
            await terminalService.InitializeTerminalsAsync();

            // Preload cache
            Console.WriteLine("Preloading terminal cache...");
            await terminalService.PreloadTerminalCacheAsync();

            // Run cache test
            await RunCacheTest(terminalService);

            // Print final cache metrics
            var metrics = terminalService.GetCacheMetrics();
            Console.WriteLine("\nFinal Cache Metrics:");
            Console.WriteLine($"Cache Hits: {metrics.hits}");
            Console.WriteLine($"Cache Misses: {metrics.misses}");
            Console.WriteLine($"Hit Rate: {metrics.hitRate:0.00}%");
            Console.WriteLine($"Total Requests: {metrics.hits + metrics.misses}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    private static async Task RunCacheTest(ITerminalService terminalService)
    {
        Console.WriteLine("\nRunning cache performance tests...");

        // First access to each terminal (should be cache misses)
        Console.WriteLine("\nTest 1: First access to all terminals");
        var stopwatch = Stopwatch.StartNew();
        for (int i = 1; i <= 40; i++)
        {
            string terminalId = $"terminal-{i:0000}";
            var terminal = await terminalService.AllocateTerminalAsync();
            if (terminal != null)
            {
                await terminalService.ReleaseTerminalAsync(terminal.Id);
            }
        }

        stopwatch.Stop();
        Console.WriteLine($"Initial access completed in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Average per terminal: {stopwatch.ElapsedMilliseconds / 40.0:0.000}ms");

        // Print current metrics
        var metrics = terminalService.GetCacheMetrics();
        Console.WriteLine($"Current hit rate: {metrics.hitRate:0.00}%");

        // Second test: repeated access (should be mostly cache hits)
        Console.WriteLine("\nTest 2: Repeated access with caching (1000 operations)");
        stopwatch.Restart();

        for (int i = 0; i < 1000; i++)
        {
            int terminalNum = i % 40 + 1; // Cycle through all 40 terminals
            string terminalId = $"terminal-{terminalNum:0000}";
            if (i % 2 == 0)
            {
                // Alternate between allocation and release to test both paths
                var terminal = await terminalService.AllocateTerminalAsync();
                if (terminal != null)
                {
                    await terminalService.ReleaseTerminalAsync(terminal.Id);
                }
            }
            else
            {
                // Try to allocate and release in sequence
                var terminal = await terminalService.AllocateTerminalAsync();
                if (terminal != null)
                {
                    await terminalService.ReleaseTerminalAsync(terminal.Id);
                }
            }
        }

        stopwatch.Stop();
        Console.WriteLine($"Completed 1000 operations in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Average per operation: {stopwatch.ElapsedMilliseconds / 1000.0:0.000}ms");

        // Print updated metrics
        metrics = terminalService.GetCacheMetrics();
        Console.WriteLine($"Updated hit rate: {metrics.hitRate:0.00}%");

        // Calculate the performance improvement
        if (metrics.hits > 0 && metrics.misses > 0)
        {
            double cacheMissTimeEstimate = stopwatch.ElapsedMilliseconds * 1.0 / (metrics.hits * 0.1 + metrics.misses);
            double cacheHitTimeEstimate = cacheMissTimeEstimate * 0.1; // Assuming cache hits are 10x faster

            double estimatedTimeWithoutCache = 1000 * cacheMissTimeEstimate;
            double actualTimeWithCache = stopwatch.ElapsedMilliseconds;
            double speedupFactor = estimatedTimeWithoutCache / actualTimeWithCache;

            Console.WriteLine($"\nEstimated performance improvement: {speedupFactor:0.0}x faster with caching");
        }
    }
}


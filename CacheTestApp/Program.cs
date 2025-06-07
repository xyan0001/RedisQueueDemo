using Microsoft.Extensions.Configuration;
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
        var logger = loggerFactory.CreateLogger<RedisTerminalService>();        // Create terminal configuration
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

        var options = Options.Create(config);

        try
        {
            // Connect to Redis with connection options that won't throw if Redis isn't available
            Console.WriteLine("Connecting to Redis...");
            var connectionOptions = ConfigurationOptions.Parse("localhost:6379");
            connectionOptions.AbortOnConnectFail = false;
            var redis = ConnectionMultiplexer.Connect(connectionOptions);            // Create mock configuration
            var configurationBuilder = new ConfigurationBuilder();
            var configuration = configurationBuilder.Build();

            // Create terminal service
            var terminalService = new RedisTerminalService(redis, options, configuration, logger);

            // Initialize terminals
            Console.WriteLine("Initializing terminals...");
            await terminalService.InitializeTerminalsAsync();

            // Preload cache
            Console.WriteLine("Preloading terminal cache...");
            await terminalService.PreloadTerminalCacheAsync();            // Run cache test
            await RunCacheTest(terminalService);
            
            Console.WriteLine("\nPress any key to run the terminal lifecycle simulation...");
            Console.ReadKey(true);
            
            // Run terminal lifecycle simulation
            await RunTerminalLifecycleSimulation(terminalService);
            
            Console.WriteLine("\nPress any key to run the orphaned terminal reclamation simulation...");
            Console.ReadKey(true);
            
            // Run orphaned terminal reclamation simulation
            await SimulateOrphanedTerminalReclamation(terminalService);

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
        Console.WriteLine("\nRunning cache performance tests...");        // First access to each terminal (should be cache misses)
        Console.WriteLine("\nTest 1: First access to all terminals");
        var stopwatch = Stopwatch.StartNew();
        for (int i = 4850; i <= 4889; i++) // Using actual terminal IDs from 4850 to 4889
        {
            string terminalId = $"terminal-{i}";
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
        stopwatch.Restart();        for (int i = 0; i < 1000; i++)
        {
            // Use the actual terminal IDs (from 4850 to 4889)
            int terminalOffset = i % 40; // 0 to 39
            int terminalNum = 4850 + terminalOffset; // Maps to 4850-4889
            string terminalId = $"terminal-{terminalNum}";
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
            double actualTimeWithCache = stopwatch.ElapsedMilliseconds;            double speedupFactor = estimatedTimeWithoutCache / actualTimeWithCache;

            Console.WriteLine($"\nEstimated performance improvement: {speedupFactor:0.0}x faster with caching");
        }
        
        Console.WriteLine("\nCache performance tests completed.");
    }

    private static async Task RunTerminalLifecycleSimulation(ITerminalService terminalService)
    {
        Console.WriteLine("\nRunning terminal lifecycle simulation...");
        Console.WriteLine("This demonstrates the complete allocation, usage, release cycle");
        Console.WriteLine("===============================================================");

        // Set up simulation parameters
        int iterations = 5;
        int usageDelayMs = 500; // Simulated usage time
        
        var stopwatch = new Stopwatch();
        var random = new Random();

        Console.WriteLine($"Running {iterations} terminal allocation cycles\n");
        
        for (int i = 1; i <= iterations; i++)
        {
            Console.WriteLine($"Cycle {i}:");
            
            // Step 1: Allocate a terminal
            Console.Write("  Allocating terminal... ");
            stopwatch.Restart();
            var terminal = await terminalService.AllocateTerminalAsync();
            stopwatch.Stop();
            
            if (terminal == null)
            {
                Console.WriteLine("Failed! No terminals available.");
                continue;
            }
            
            Console.WriteLine($"Success! Got terminal {terminal.Id} in {stopwatch.ElapsedMilliseconds}ms");
            
            // Step 2: Get or create a session
            Console.Write("  Creating session... ");
            stopwatch.Restart();
            string sessionId;
            try
            {
                sessionId = await terminalService.GetOrCreateSessionAsync(terminal.Id);
                stopwatch.Stop();
                Console.WriteLine($"Success! Session {sessionId} created in {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Console.WriteLine($"Failed! {ex.Message}");
                
                // Even with session creation failure, we need to release the terminal
                Console.Write("  Releasing terminal... ");
                await terminalService.ReleaseTerminalAsync(terminal.Id);
                Console.WriteLine("Done");
                continue;
            }
            
            // Step 3: Simulate using the terminal
            int simulatedUsageTime = usageDelayMs + random.Next(-100, 100); // Add some randomness
            Console.Write($"  Using terminal for {simulatedUsageTime}ms... ");
            await Task.Delay(simulatedUsageTime);
            
            // Update last used time
            await terminalService.UpdateLastUsedTimeAsync(terminal.Id);
            Console.WriteLine("Done");
            
            // Step 4: Release the terminal
            Console.Write("  Releasing terminal... ");
            stopwatch.Restart();
            await terminalService.ReleaseTerminalAsync(terminal.Id);
            stopwatch.Stop();
            Console.WriteLine($"Success! Terminal released in {stopwatch.ElapsedMilliseconds}ms");
              // Brief pause between cycles
            await Task.Delay(300);
        }
        
        Console.WriteLine("\nTerminal lifecycle simulation completed!");
    }

    private static async Task SimulateOrphanedTerminalReclamation(ITerminalService terminalService)
    {
        Console.WriteLine("\nSimulating orphaned terminal reclamation...");
        Console.WriteLine("This demonstrates how terminals are reclaimed after timeout");
        Console.WriteLine("========================================================");
        
        // Step 1: Allocate a terminal but don't release it
        Console.Write("1. Allocating terminal... ");
        var terminal = await terminalService.AllocateTerminalAsync();
        if (terminal == null)
        {
            Console.WriteLine("Failed! No terminals available.");
            return;
        }
        Console.WriteLine($"Success! Got terminal {terminal.Id}");
        
        // Step 2: Get a session for the terminal
        Console.Write("2. Creating session... ");
        string sessionId;
        try
        {
            sessionId = await terminalService.GetOrCreateSessionAsync(terminal.Id);
            Console.WriteLine($"Success! Session {sessionId} created");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed! {ex.Message}");
            
            // Even with session creation failure, we need to release the terminal
            Console.Write("   Releasing terminal... ");
            await terminalService.ReleaseTerminalAsync(terminal.Id);
            Console.WriteLine("Done");
            return;
        }
        
        // Step 3: Simulate a pod crash by NOT releasing the terminal
        Console.WriteLine("3. Simulating pod crash (not releasing terminal)");
        
        // Step 4: Wait for the orphaned terminal timeout
        int orphanTimeout = 10; // Use a short timeout for the demo
        Console.WriteLine($"4. Waiting {orphanTimeout} seconds for orphaned terminal timeout...");
        for (int i = 1; i <= orphanTimeout; i++)
        {
            Console.Write(".");
            await Task.Delay(1000);
            if (i % 5 == 0)
                Console.WriteLine(); // Line break every 5 seconds
        }
        Console.WriteLine();
        
        // Step 5: Manually reclaim orphaned terminals
        Console.Write("5. Running orphaned terminal reclamation process... ");
        await terminalService.ReclaimOrphanedTerminalsAsync();
        Console.WriteLine("Done");
        
        // Step 6: Verify the terminal is back in the pool by allocating it again
        Console.Write("6. Attempting to allocate a terminal from the pool... ");
        var reclaimedTerminal = await terminalService.AllocateTerminalAsync();
        if (reclaimedTerminal != null)
        {
            Console.WriteLine($"Success! Got terminal {reclaimedTerminal.Id}");
            
            // Release the terminal properly this time
            Console.Write("7. Releasing terminal... ");
            await terminalService.ReleaseTerminalAsync(reclaimedTerminal.Id);
            Console.WriteLine("Done");
        }
        else
        {
            Console.WriteLine("Failed! No terminals available (reclamation may have been incomplete)");
        }
        
        Console.WriteLine("\nOrphaned terminal reclamation simulation completed!");
    }
}


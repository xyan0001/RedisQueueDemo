using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TerminalManagementService.Models;
using TerminalManagementService.Services;

namespace RedisQueueDemo
{
    public class CacheTest
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
                // Connect to Redis
                Console.WriteLine("Connecting to Redis...");
                var redis = ConnectionMultiplexer.Connect("localhost:6379");
                
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
                
                // Get cache metrics
                var metrics = terminalService.GetCacheMetrics();
                Console.WriteLine($"Cache Hits: {metrics.hits}");
                Console.WriteLine($"Cache Misses: {metrics.misses}");
                Console.WriteLine($"Hit Rate: {metrics.hitRate:0.00}%");
                Console.WriteLine($"Total Requests: {metrics.hits + metrics.misses}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
        
        private static async Task RunCacheTest(RedisTerminalService terminalService)
        {
            Console.WriteLine("Running cache test...");
            
            // Get all terminals to ensure they're in the cache
            for (int i = 1; i <= 40; i++)
            {
                string terminalId = $"terminal-{i:0000}";
                await terminalService.GetTerminalInfoAsync(terminalId);
            }
            
            // Run performance test with cache
            Console.WriteLine("Testing with cache...");
            var stopwatch = Stopwatch.StartNew();
            
            for (int i = 0; i < 1000; i++)
            {
                int terminalNum = i % 40 + 1; // Cycle through all 40 terminals
                string terminalId = $"terminal-{terminalNum:0000}";
                await terminalService.GetTerminalInfoAsync(terminalId);
            }
            
            stopwatch.Stop();
            Console.WriteLine($"With cache: 1000 lookups in {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"Average per lookup: {stopwatch.ElapsedMilliseconds / 1000.0:0.000}ms");
            
            // Run one more test to verify cache is working correctly
            var metrics = terminalService.GetCacheMetrics();
            Console.WriteLine($"Cache hit rate after test: {metrics.hitRate:0.00}%");
        }
    }
}
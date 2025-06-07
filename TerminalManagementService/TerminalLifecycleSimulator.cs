using System.Diagnostics;
using System.Text.Json;
using TerminalManagementService.Models;
using TerminalManagementService.Services;

namespace TerminalManagementService;

/// <summary>
/// Terminal Lifecycle Simulator - demonstrates the terminal allocation, usage, and release cycle
/// </summary>
public class TerminalLifecycleSimulator
{
    private readonly ITerminalService _terminalService;
    private readonly ILogger<TerminalLifecycleSimulator> _logger;
    private readonly Stopwatch _stopwatch = new();

    // Statistics tracking
    private int _successfulOperations = 0;
    private int _failedOperations = 0;
    private List<double> _operationTimes = new();
    private readonly object _lockObject = new();

    public TerminalLifecycleSimulator(
        ITerminalService terminalService,
        ILogger<TerminalLifecycleSimulator> logger)
    {
        _terminalService = terminalService ?? throw new ArgumentNullException(nameof(terminalService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Run a simulation of multiple terminal lifecycle operations
    /// </summary>
    /// <param name="iterations">Number of operations to perform</param>
    /// <param name="parallelism">Number of parallel operations</param>
    /// <param name="simulatedUsageTimeMs">Time to simulate terminal usage (in ms)</param>
    /// <returns>Simulation results</returns>
    public async Task<SimulationResult> RunSimulationAsync(
        int iterations = 100,
        int parallelism = 10,
        int simulatedUsageTimeMs = 200)
    {
        // Reset counters
        _successfulOperations = 0;
        _failedOperations = 0;
        _operationTimes.Clear();

        _logger.LogInformation(
            "Starting terminal lifecycle simulation: {Iterations} iterations, {Parallelism} parallel operations, {UsageTime}ms usage time",
            iterations, parallelism, simulatedUsageTimeMs);

        _stopwatch.Restart();

        // Create tasks for each operation
        var tasks = new List<Task>();
        for (int i = 0; i < iterations; i++)
        {
            // Control parallelism by limiting concurrent tasks
            if (tasks.Count >= parallelism)
            {
                // Wait for any task to complete before adding more
                await Task.WhenAny(tasks.ToArray());
                tasks.RemoveAll(t => t.IsCompleted);
            }

            tasks.Add(SimulateTerminalLifecycleAsync(simulatedUsageTimeMs));
        }

        // Wait for all remaining tasks to complete
        await Task.WhenAll(tasks);

        _stopwatch.Stop();

        // Calculate statistics
        var result = new SimulationResult
        {
            TotalOperations = _successfulOperations + _failedOperations,
            SuccessfulOperations = _successfulOperations,
            FailedOperations = _failedOperations,
            TotalDurationMs = _stopwatch.ElapsedMilliseconds,
            AverageOperationTimeMs = _operationTimes.Count > 0 ? _operationTimes.Average() : 0,
            MinOperationTimeMs = _operationTimes.Count > 0 ? _operationTimes.Min() : 0,
            MaxOperationTimeMs = _operationTimes.Count > 0 ? _operationTimes.Max() : 0,
            OperationsPerSecond = _successfulOperations / (_stopwatch.ElapsedMilliseconds / 1000.0),
            Parallelism = parallelism,
            SimulatedUsageTimeMs = simulatedUsageTimeMs
        };

        _logger.LogInformation(
            "Simulation completed: {Success} successful, {Failed} failed, {Duration}ms total duration, {Rate:F2} ops/sec",
            result.SuccessfulOperations,
            result.FailedOperations,
            result.TotalDurationMs,
            result.OperationsPerSecond);

        return result;
    }

    /// <summary>
    /// Simulate a single terminal lifecycle (allocate, use, release)
    /// </summary>
    private async Task SimulateTerminalLifecycleAsync(int simulatedUsageTimeMs)
    {
        var operationStopwatch = new Stopwatch();
        operationStopwatch.Start();

        try
        {
            // Step 1: Allocate a terminal
            var terminal = await _terminalService.AllocateTerminalAsync();
            if (terminal == null)
            {
                _logger.LogWarning("Failed to allocate terminal - no terminals available");
                Interlocked.Increment(ref _failedOperations);
                return;
            }

            // Step 2: Get or create a session
            var sessionId = await _terminalService.GetOrCreateSessionAsync(terminal.Id);

            // Step 3: Simulate using the terminal (making a request)
            await Task.Delay(simulatedUsageTimeMs);
            
            // Update last used time to show activity
            await _terminalService.UpdateLastUsedTimeAsync(terminal.Id);
            
            // Step 4: Release the terminal back to the pool
            await _terminalService.ReleaseTerminalAsync(terminal.Id);

            operationStopwatch.Stop();

            // Record successful operation
            lock (_lockObject)
            {
                _operationTimes.Add(operationStopwatch.ElapsedMilliseconds);
            }
            Interlocked.Increment(ref _successfulOperations);
        }
        catch (Exception ex)
        {
            operationStopwatch.Stop();
            _logger.LogError(ex, "Error during terminal lifecycle simulation");
            Interlocked.Increment(ref _failedOperations);
        }
    }

    /// <summary>
    /// Get current simulation statistics
    /// </summary>
    public SimulationResult GetCurrentStatistics()
    {
        var elapsedMs = _stopwatch.ElapsedMilliseconds;
        return new SimulationResult
        {
            TotalOperations = _successfulOperations + _failedOperations,
            SuccessfulOperations = _successfulOperations,
            FailedOperations = _failedOperations,
            TotalDurationMs = elapsedMs,
            AverageOperationTimeMs = _operationTimes.Count > 0 ? _operationTimes.Average() : 0,
            MinOperationTimeMs = _operationTimes.Count > 0 ? _operationTimes.Min() : 0,
            MaxOperationTimeMs = _operationTimes.Count > 0 ? _operationTimes.Max() : 0,
            OperationsPerSecond = elapsedMs > 0 ? _successfulOperations / (elapsedMs / 1000.0) : 0
        };
    }
}

/// <summary>
/// Results of a terminal lifecycle simulation
/// </summary>
public class SimulationResult
{
    public int TotalOperations { get; set; }
    public int SuccessfulOperations { get; set; }
    public int FailedOperations { get; set; }
    public double TotalDurationMs { get; set; }
    public double AverageOperationTimeMs { get; set; }
    public double MinOperationTimeMs { get; set; }
    public double MaxOperationTimeMs { get; set; }
    public double OperationsPerSecond { get; set; }
    public int Parallelism { get; set; }
    public int SimulatedUsageTimeMs { get; set; }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }
}

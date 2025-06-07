using Microsoft.Extensions.Options;
using TerminalManagementService.Models;
using TerminalManagementService.Services;

namespace TerminalManagementService.BackgroundServices;

/// <summary>
/// Background service to handle terminal cleanup and orphan reclamation
/// </summary>
public class TerminalCleanupService(
    ITerminalService terminalService,
    IOptions<TerminalConfiguration> config,
    ILogger<TerminalCleanupService> logger)
    : BackgroundService
{
    private readonly ITerminalService _terminalService = terminalService ?? throw new ArgumentNullException(nameof(terminalService));
    private readonly ILogger<TerminalCleanupService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TerminalConfiguration _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Terminal cleanup service is starting");

        try
        {
            // Initialize terminals when the service starts
            await _terminalService.InitializeTerminalsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing terminals");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Terminal cleanup cycle starting");

            try
            {
                // Reclaim any orphaned terminals
                await _terminalService.ReclaimOrphanedTerminalsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during terminal cleanup cycle");
            }

            await Task.Delay(_cleanupInterval, stoppingToken);
        }

        _logger.LogInformation("Terminal cleanup service is stopping");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Terminal cleanup service is releasing terminals on shutdown");

        try
        {
            // Release all terminals allocated by this pod
            await _terminalService.ShutdownAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing terminals during shutdown");
        }

        await base.StopAsync(cancellationToken);
    }
}

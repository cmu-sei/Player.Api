// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license, please see LICENSE.md in the project root for license information or contact permission@sei.cmu.edu for full terms.

using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Player.Api.Infrastructure.Options;

namespace Player.Api.Services;

public class XApiBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<XApiBackgroundService> _logger;
    private const int ProcessingDelaySeconds = 5;
    private const int BatchSize = 10;
    private const int CleanupDelayHours = 24;

    public XApiBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<XApiBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("xAPI Background Service started");

        // Check if xAPI is configured (get from scope)
        using (var scope = _serviceProvider.CreateScope())
        {
            var xApiOptions = scope.ServiceProvider.GetRequiredService<XApiOptions>();
            if (string.IsNullOrWhiteSpace(xApiOptions.Username))
            {
                _logger.LogInformation("xAPI is not configured. Background service will not process statements.");
                return;
            }
        }

        DateTime lastCleanup = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueueAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing xAPI queue");
            }

            if (DateTime.UtcNow - lastCleanup >= TimeSpan.FromHours(CleanupDelayHours))
            {
                try
                {
                    await CleanupOldStatementsAsync(stoppingToken);
                    lastCleanup = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cleaning up old xAPI statements");
                }
            }

            // Wait before processing next batch
            await Task.Delay(TimeSpan.FromSeconds(ProcessingDelaySeconds), stoppingToken);
        }

        _logger.LogInformation("xAPI Background Service stopped");
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var queueService = scope.ServiceProvider.GetRequiredService<IXApiQueueService>();
        var xApiOptions = scope.ServiceProvider.GetRequiredService<XApiOptions>();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        // Get a batch of statements to process
        var statements = await queueService.DequeueAsync(BatchSize, cancellationToken);

        if (statements.Count == 0)
        {
            return; // Nothing to process
        }

        // Create HTTP client for LRS
        var httpClient = httpClientFactory.CreateClient();
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{xApiOptions.Username}:{xApiOptions.Password}"));
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
        httpClient.DefaultRequestHeaders.Add("X-Experience-API-Version", "1.0.3");

        // Process each statement
        foreach (var queuedStatement in statements)
        {
            try
            {
                // Send raw JSON directly to LRS
                var content = new StringContent(queuedStatement.StatementJson, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync($"{xApiOptions.Endpoint}/statements", content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    await queueService.MarkCompletedAsync(queuedStatement.Id, cancellationToken);
                    _logger.LogInformation("Successfully sent xAPI statement {StatementId} to LRS", queuedStatement.Id);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    await queueService.MarkFailedAsync(queuedStatement.Id, $"HTTP {response.StatusCode}: {errorBody}", cancellationToken);
                    _logger.LogWarning("Failed to send xAPI statement {StatementId}: HTTP {StatusCode} - {Error}",
                        queuedStatement.Id, response.StatusCode, errorBody);
                }
            }
            catch (Exception ex)
            {
                await queueService.MarkFailedAsync(queuedStatement.Id, ex.Message, cancellationToken);
                _logger.LogError(ex, "Error processing xAPI statement {StatementId}", queuedStatement.Id);
            }
        }
    }

    private async Task CleanupOldStatementsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var queueService = scope.ServiceProvider.GetRequiredService<IXApiQueueService>();
        var xApiOptions = scope.ServiceProvider.GetRequiredService<XApiOptions>();

        var cutoffDate = DateTime.UtcNow.AddDays(-xApiOptions.RetentionDays);

        _logger.LogInformation("Cleaning up xAPI statements older than {CutoffDate} (Retention: {RetentionDays} days)",
            cutoffDate, xApiOptions.RetentionDays);

        var completedStatements = await queueService.GetOldCompletedStatementsAsync(cutoffDate, cancellationToken);
        var failedStatements = await queueService.GetOldFailedStatementsAsync(cutoffDate, cancellationToken);

        var totalStatements = completedStatements.Count + failedStatements.Count;

        if (totalStatements == 0)
        {
            _logger.LogInformation("No old statements to cleanup");
            return;
        }

        var allStatementIds = completedStatements.Select(s => s.Id)
            .Concat(failedStatements.Select(s => s.Id))
            .ToList();

        await queueService.DeleteStatementsAsync(allStatementIds, cancellationToken);

        _logger.LogInformation("Deleted {Count} old xAPI statements", totalStatements);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("xAPI Background Service is stopping");
        return base.StopAsync(cancellationToken);
    }
}

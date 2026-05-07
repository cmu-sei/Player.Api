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

        // Check if xAPI is configured and get processing delay (get from scope)
        int processingDelaySeconds;
        using (var scope = _serviceProvider.CreateScope())
        {
            var xApiOptions = scope.ServiceProvider.GetRequiredService<XApiOptions>();
            if (string.IsNullOrWhiteSpace(xApiOptions.Username))
            {
                _logger.LogInformation("xAPI is not configured. Background service will not process statements.");
                return;
            }
            processingDelaySeconds = xApiOptions.ProcessingDelaySeconds > 0 ? xApiOptions.ProcessingDelaySeconds : 30;
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
            await Task.Delay(TimeSpan.FromSeconds(processingDelaySeconds), stoppingToken);
        }

        _logger.LogInformation("xAPI Background Service stopped");
    }

    private static bool IsTransientError(HttpResponseMessage response, Exception ex)
    {
        // Check HTTP status codes
        if (response != null)
        {
            var statusCode = (int)response.StatusCode;

            // Transient HTTP errors
            if (statusCode == 429 || statusCode == 500 || statusCode == 502 ||
                statusCode == 503 || statusCode == 504)
            {
                return true;
            }

            // Permanent HTTP errors
            if (statusCode == 400 || statusCode == 401 || statusCode == 403 || statusCode == 422)
            {
                return false;
            }

            // Any other non-success status: treat as transient (conservative approach)
            return true;
        }

        // Check exception types
        if (ex != null)
        {
            // Timeout and network errors are transient
            if (ex is TaskCanceledException ||
                ex is OperationCanceledException ||
                ex is HttpRequestException)
            {
                return true;
            }

            // Unknown exceptions: treat as transient (conservative approach)
            return true;
        }

        return true; // Default to transient
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
            HttpResponseMessage response = null;
            Exception caughtException = null;

            try
            {
                // Send raw JSON directly to LRS
                var content = new StringContent(queuedStatement.StatementJson, Encoding.UTF8, "application/json");
                response = await httpClient.PostAsync($"{xApiOptions.Endpoint}/statements", content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    await queueService.MarkCompletedAsync(queuedStatement.Id, cancellationToken);
                    _logger.LogInformation("Successfully sent xAPI statement {StatementId} to LRS", queuedStatement.Id);
                }
                else if ((int)response.StatusCode == 409)
                {
                    // HTTP 409 Conflict - statement already exists, treat as success
                    await queueService.MarkCompletedAsync(queuedStatement.Id, cancellationToken);
                    _logger.LogInformation(
                        "xAPI statement {StatementId} already exists in LRS (HTTP 409), marking as completed",
                        queuedStatement.Id);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    bool isTransient = IsTransientError(response, null);
                    await queueService.MarkFailedAsync(
                        queuedStatement.Id,
                        $"HTTP {response.StatusCode}: {errorBody}",
                        isTransient,
                        cancellationToken);

                    var logLevel = isTransient ? LogLevel.Warning : LogLevel.Error;
                    _logger.Log(logLevel,
                        "Failed to send xAPI statement {StatementId}: HTTP {StatusCode} ({ErrorType}) - {Error}",
                        queuedStatement.Id, response.StatusCode, isTransient ? "Transient" : "Permanent", errorBody);
                }
            }
            catch (Exception ex)
            {
                caughtException = ex;
                bool isTransient = IsTransientError(null, ex);
                await queueService.MarkFailedAsync(
                    queuedStatement.Id,
                    $"{ex.GetType().Name}: {ex.Message}",
                    isTransient,
                    cancellationToken);

                var logLevel = isTransient ? LogLevel.Warning : LogLevel.Error;
                _logger.Log(logLevel, ex,
                    "Error processing xAPI statement {StatementId} ({ErrorType})",
                    queuedStatement.Id, isTransient ? "Transient" : "Permanent");
            }
        }
    }

    private async Task CleanupOldStatementsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var queueService = scope.ServiceProvider.GetRequiredService<IXApiQueueService>();
        var xApiOptions = scope.ServiceProvider.GetRequiredService<XApiOptions>();

        var retentionCutoff = DateTime.UtcNow.AddDays(-xApiOptions.RetentionDays);
        var processingStuckThreshold = DateTime.UtcNow.AddMinutes(-xApiOptions.ProcessingTimeoutMinutes);

        _logger.LogInformation(
            "Starting xAPI cleanup - RetentionCutoff: {RetentionCutoff}, ProcessingStuckThreshold: {ProcessingStuckThreshold}",
            retentionCutoff, processingStuckThreshold);

        var completedStatements = await queueService.GetOldCompletedStatementsAsync(retentionCutoff, cancellationToken);
        var failedStatements = await queueService.GetOldFailedStatementsAsync(retentionCutoff, cancellationToken);
        var stuckProcessingStatements = await queueService.GetStuckProcessingStatementsAsync(processingStuckThreshold, cancellationToken);

        var totalStatements = completedStatements.Count + failedStatements.Count + stuckProcessingStatements.Count;

        if (totalStatements == 0)
        {
            _logger.LogInformation("No statements to cleanup");
            return;
        }

        // Log detailed breakdown for visibility
        _logger.LogWarning(
            "Cleanup summary - Completed: {Completed}, Failed: {Failed}, StuckProcessing: {StuckTotal}",
            completedStatements.Count,
            failedStatements.Count,
            stuckProcessingStatements.Count);

        var allStatementIds = completedStatements.Select(s => s.Id)
            .Concat(failedStatements.Select(s => s.Id))
            .Concat(stuckProcessingStatements.Select(s => s.Id))
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

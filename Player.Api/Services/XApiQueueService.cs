// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license, please see LICENSE.md in the project root for license information or contact permission@sei.cmu.edu for full terms.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;

namespace Player.Api.Services;

public interface IXApiQueueService
{
    Task EnqueueAsync(XApiQueuedStatementEntity statement, CancellationToken ct = default);
    Task<List<XApiQueuedStatementEntity>> DequeueAsync(int batchSize = 10, CancellationToken ct = default);
    Task MarkCompletedAsync(Guid statementId, CancellationToken ct = default);
    Task MarkFailedAsync(Guid statementId, string errorMessage, bool isTransientError, CancellationToken ct = default);
    Task<int> GetQueueDepthAsync(CancellationToken ct = default);
    Task<List<XApiQueuedStatementEntity>> GetOldCompletedStatementsAsync(DateTime cutoffDate, CancellationToken ct = default);
    Task<List<XApiQueuedStatementEntity>> GetOldFailedStatementsAsync(DateTime cutoffDate, CancellationToken ct = default);
    Task<List<XApiQueuedStatementEntity>> GetStuckProcessingStatementsAsync(DateTime stuckThreshold, CancellationToken ct = default);
    Task DeleteStatementsAsync(List<Guid> statementIds, CancellationToken ct = default);
}

public class XApiQueueService : IXApiQueueService
{
    private readonly PlayerContext _context;
    private readonly ILogger<XApiQueueService> _logger;

    public XApiQueueService(
        PlayerContext context,
        ILogger<XApiQueueService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task EnqueueAsync(XApiQueuedStatementEntity statement, CancellationToken ct = default)
    {
        
        statement.QueuedAt = DateTime.UtcNow;
        statement.Status = XApiQueueStatus.Pending;
        statement.RetryCount = 0;

        _context.XApiQueuedStatements.Add(statement);
        await _context.SaveChangesAsync(ct);

        _logger.LogDebug("Enqueued xAPI statement {StatementId} for verb {Verb}", statement.Id, statement.Verb);
    }

    public async Task<List<XApiQueuedStatementEntity>> DequeueAsync(int batchSize = 10, CancellationToken ct = default)
    {
        
        // Get pending statements (no retry count limit - transient errors retry indefinitely)
        var statements = await _context.XApiQueuedStatements
            .Where(s => s.Status == XApiQueueStatus.Pending)
            .OrderBy(s => s.QueuedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        // Mark them as processing
        foreach (var statement in statements)
        {
            statement.Status = XApiQueueStatus.Processing;
            statement.LastAttemptAt = DateTime.UtcNow;
            statement.RetryCount++;
        }

        if (statements.Any())
        {
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Dequeued {Count} xAPI statements for processing", statements.Count);
        }

        return statements;
    }

    public async Task MarkCompletedAsync(Guid statementId, CancellationToken ct = default)
    {
        
        var statement = await _context.XApiQueuedStatements.FindAsync(new object[] { statementId }, ct);
        if (statement == null)
        {
            _logger.LogWarning("Attempted to mark non-existent statement {StatementId} as completed", statementId);
            return;
        }

        statement.Status = XApiQueueStatus.Completed;
        await _context.SaveChangesAsync(ct);

        _logger.LogDebug("Marked xAPI statement {StatementId} as completed", statementId);
    }

    public async Task MarkFailedAsync(Guid statementId, string errorMessage, bool isTransientError, CancellationToken ct = default)
    {
        
        var statement = await _context.XApiQueuedStatements.FindAsync(new object[] { statementId }, ct);
        if (statement == null)
        {
            _logger.LogWarning("Attempted to mark non-existent statement {StatementId} as failed", statementId);
            return;
        }

        if (isTransientError)
        {
            // Transient error - keep retrying indefinitely
            statement.Status = XApiQueueStatus.Pending;
            statement.ErrorMessage = $"[TRANSIENT] {errorMessage}";

            await _context.SaveChangesAsync(ct);

            _logger.LogWarning(
                "xAPI statement {StatementId} encountered transient error on attempt {RetryCount}, will retry: {Error}",
                statementId, statement.RetryCount, errorMessage);
        }
        else
        {
            // Permanent error - mark as failed immediately
            statement.Status = XApiQueueStatus.Failed;
            statement.ErrorMessage = $"[PERMANENT] {errorMessage}";

            await _context.SaveChangesAsync(ct);

            _logger.LogError(
                "xAPI statement {StatementId} encountered permanent error after {RetryCount} attempts: {Error}",
                statementId, statement.RetryCount, errorMessage);
        }
    }

    public async Task<int> GetQueueDepthAsync(CancellationToken ct = default)
    {
        
        return await _context.XApiQueuedStatements
            .CountAsync(s => s.Status == XApiQueueStatus.Pending || s.Status == XApiQueueStatus.Processing, ct);
    }

    public async Task<List<XApiQueuedStatementEntity>> GetOldCompletedStatementsAsync(DateTime cutoffDate, CancellationToken ct = default)
    {
        
        return await _context.XApiQueuedStatements
            .Where(s => s.Status == XApiQueueStatus.Completed && s.QueuedAt < cutoffDate)
            .ToListAsync(ct);
    }

    public async Task<List<XApiQueuedStatementEntity>> GetOldFailedStatementsAsync(DateTime cutoffDate, CancellationToken ct = default)
    {
        
        return await _context.XApiQueuedStatements
            .Where(s => s.Status == XApiQueueStatus.Failed && s.QueuedAt < cutoffDate)
            .ToListAsync(ct);
    }

    public async Task<List<XApiQueuedStatementEntity>> GetStuckProcessingStatementsAsync(DateTime stuckThreshold, CancellationToken ct = default)
    {
        
        return await _context.XApiQueuedStatements
            .Where(s => s.Status == XApiQueueStatus.Processing
                     && s.LastAttemptAt.HasValue
                     && s.LastAttemptAt.Value < stuckThreshold)
            .ToListAsync(ct);
    }

    public async Task DeleteStatementsAsync(List<Guid> statementIds, CancellationToken ct = default)
    {
        
        var statements = await _context.XApiQueuedStatements
            .Where(s => statementIds.Contains(s.Id))
            .ToListAsync(ct);

        _context.XApiQueuedStatements.RemoveRange(statements);
        await _context.SaveChangesAsync(ct);
    }
}

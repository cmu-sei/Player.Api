// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license, please see LICENSE.md in the project root for license information or contact permission@sei.cmu.edu for full terms.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Player.Api.Data.Data.Models;

public class XApiQueuedStatementEntity : IEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    [Required]
    public string StatementJson { get; set; }

    [Required]
    public DateTime QueuedAt { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public int RetryCount { get; set; }

    [Required]
    public XApiQueueStatus Status { get; set; }

    public string ErrorMessage { get; set; }

    // Optional: Store some metadata for debugging/monitoring
    public string Verb { get; set; }
    public string ActivityId { get; set; }
    public Guid? ViewId { get; set; }
}

public enum XApiQueueStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

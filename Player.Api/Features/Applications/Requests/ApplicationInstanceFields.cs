// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Player.Api.Features.Applications;

/// <summary>
/// The fields an Application Instance's create and edit commands share. The owning Team is not one of
/// them: only <see cref="CreateApplicationInstance.Command"/> names it, so an edit cannot move an
/// Application Instance to another Team.
/// </summary>
public abstract class ApplicationInstanceFields : IRequest<ApplicationInstance>
{
    [Required]
    public Guid ApplicationId { get; set; }

    public float DisplayOrder { get; set; }
}

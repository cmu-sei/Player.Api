// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Player.Api.Infrastructure.JsonConverters;

namespace Player.Api.Features.Applications;

/// <summary>
/// The fields an Application's create and edit commands share. The owning View is not one of them:
/// only <see cref="Create.Command"/> names it, so an edit cannot move an Application to another View.
/// </summary>
public abstract record ApplicationFields : IRequest<Application>
{
    public string Name { get; set; }

    [Url]
    public string Url { get; set; }

    public string Icon { get; set; }

    [JsonConverter(typeof(StringToBooleanConverter))]
    public bool? Embeddable { get; set; }

    [JsonConverter(typeof(StringToBooleanConverter))]
    public bool? LoadInBackground { get; set; }

    public Guid? ApplicationTemplateId { get; set; }
}

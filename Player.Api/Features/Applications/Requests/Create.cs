// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Views;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.Applications;

public class Create
{
    [DataContract(Name = "CreateApplicationCommand")]
    public record Command : IRequest<Application>
    {
        public string Name { get; set; }

        [Url]
        public string Url { get; set; }

        public string Icon { get; set; }

        public bool? Embeddable { get; set; }
        public bool? LoadInBackground { get; set; }

        [Required]
        public Guid ViewId { get; set; }

        public Guid? ApplicationTemplateId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("views/{viewId}/applications", TypedHandler)
                    .WithName("createApplication")
                    .WithDescription("Creates a new Application within a View with the attributes specified. An Application Template is a top-level resource that can optionally be the parent of an View specific Application resource, which would inherit it's properties")
                    .WithSummary("Creates a new Application within a View.")
            ];
        }

        async Task<CreatedAtRoute<Application>> TypedHandler(Guid viewId, Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.ViewId = viewId;
            var createdApplication = await mediator.Send(command, cancellationToken);
            return TypedResults.CreatedAtRoute(createdApplication, "getApplication", new { id = createdApplication.Id });
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, Application>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ViewEntity>(request.ViewId, [SystemPermission.ManageApplications], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<Application> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var viewExists = await db.Views.Where(e => e.Id == request.ViewId).AnyAsync(cancellationToken);

            if (!viewExists)
                throw new EntityNotFoundException<View>();

            var applicationEntity = mapper.Map<ApplicationEntity>(request);

            db.Applications.Add(applicationEntity);
            await db.SaveChangesAsync(cancellationToken);

            var application = mapper.Map<Application>(applicationEntity);
            return application;
        }
    }
}
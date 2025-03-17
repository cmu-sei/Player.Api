// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.Applications;

public class Edit
{
    [DataContract(Name = "EditApplicationCommand")]
    public record Command : Create.Command
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPut("applications/{id}", TypedHandler)
                    .WithName("updateApplication")
                    .WithDescription("Updates an Application with the attributes specified")
                    .WithSummary("Updates an Application.")
            ];
        }

        async Task<Ok<Application>> TypedHandler([FromRoute] Guid id, [FromBody] Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.Id = id;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, Application>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ViewEntity>(request.ViewId, [SystemPermission.ManageApplications], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<Application> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var applicationToUpdate = await db.Applications.SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (applicationToUpdate == null)
                throw new EntityNotFoundException<Application>();

            mapper.Map(request, applicationToUpdate);

            db.Applications.Update(applicationToUpdate);
            await db.SaveChangesAsync(cancellationToken);

            return mapper.Map<Application>(applicationToUpdate);
        }
    }
}
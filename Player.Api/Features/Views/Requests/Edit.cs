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
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.Views;

public class Edit
{
    [DataContract(Name = "EditViewCommand")]
    public record Command : IRequest<View>
    {
        [JsonIgnore]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ViewStatus Status { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPut("views/{id}", TypedHandler)
                    .WithName("updateView")
                    .WithDescription("Updates a View with the attributes specified.")
                    .WithSummary("Updates a View.")
            ];
        }

        async Task<Ok<View>> TypedHandler(Guid id, Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.Id = id;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, View>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ViewEntity>(request.Id, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<View> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var viewToUpdate = await db.Views
                .SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (viewToUpdate == null)
                throw new EntityNotFoundException<View>();

            mapper.Map(request, viewToUpdate);

            db.Views.Update(viewToUpdate);
            await db.SaveChangesAsync(cancellationToken);

            return mapper.Map<View>(viewToUpdate);
        }
    }
}
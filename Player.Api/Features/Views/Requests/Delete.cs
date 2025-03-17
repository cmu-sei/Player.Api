// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
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
using Player.Api.Services;

namespace Player.Api.Features.Views;

public class Delete
{
    [DataContract(Name = "DeleteViewCommand")]
    public class Command : IRequest
    {
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapDelete("views/{id}", TypedHandler)
                    .WithName("deleteView")
                    .WithDescription("Deletes a View with the specified id.")
                    .WithSummary("Deletes a View.")
            ];
        }

        async Task<NoContent> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            await mediator.Send(new Command { Id = id }, cancellationToken);
            return TypedResults.NoContent();
        }
    }

    public class Handler(IFileService fileService, IPlayerAuthorizationService authorizationService, PlayerContext db) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ViewEntity>(request.Id, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var viewToDelete = await db.Views
                .SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (viewToDelete == null)
                throw new EntityNotFoundException<View>();

            // Delete files within this view
            var files = await fileService.GetByViewAsync(request.Id, cancellationToken);
            foreach (var fp in files)
            {
                await fileService.DeleteAsync(fp.id, cancellationToken);
            }

            db.Views.Remove(viewToDelete);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
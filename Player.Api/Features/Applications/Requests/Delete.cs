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
using Player.Api.ViewModels;

namespace Player.Api.Features.Applications;

public class Delete
{
    [DataContract(Name = "DeleteApplicationCommand")]
    public class Command : IRequest
    {
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapDelete("applications/{id}", TypedHandler)
                    .WithName("deleteApplication")
                    .WithDescription("Deletes an Application with the specified id.")
                    .WithSummary("Deletes an Application.")
            ];
        }

        async Task<NoContent> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            await mediator.Send(new Command { Id = id }, cancellationToken);
            return TypedResults.NoContent();
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ApplicationEntity>(request.Id, [SystemPermission.ManageApplications], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var applicationToDelete = await db.Applications.SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (applicationToDelete == null)
                throw new EntityNotFoundException<Application>();

            db.Applications.Remove(applicationToDelete);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
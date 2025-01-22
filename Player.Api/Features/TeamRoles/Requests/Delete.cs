using System;
using System.Linq;
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
using Player.Api.Options;

namespace Player.Api.Features.TeamRoles;

public class Delete
{
    [DataContract(Name = "DeleteTeamRoleCommand")]
    public class Command : IRequest
    {
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapDelete("team-roles/{id}", TypedHandler)
                    .WithName("deleteTeamRole")
                    .WithDescription("Deletes a Team Role with the specified id.")
                    .WithSummary("Deletes a Team Role.")
            ];
        }

        async Task<NoContent> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            await mediator.Send(new Command { Id = id }, cancellationToken);
            return TypedResults.NoContent();
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, RoleOptions roleOptions) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageRoles], [], [], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var roleToDelete = await db.TeamRoles.SingleOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

            if (roleToDelete == null)
                throw new EntityNotFoundException<TeamRole>();

            var defaultRoleIds = await db.TeamRoles
                .Where(x => x.Name == roleOptions.DefaultTeamRole || x.Name == roleOptions.DefaultViewCreatorRole)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken);

            if (defaultRoleIds.Contains(roleToDelete.Id))
                throw new ConflictException($"Cannot delete the {nameof(roleOptions.DefaultTeamRole)} or {roleOptions.DefaultViewCreatorRole}");

            db.TeamRoles.Remove(roleToDelete);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
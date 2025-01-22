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
using Player.Api.Features.Teams;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.TeamPermissions;

public class RemoveFromTeam
{
    [DataContract(Name = "RemoveTeamPermissionFromTeamCommand")]
    public record Command : IRequest
    {
        public Guid TeamId { get; set; }
        public Guid TeamPermissionId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapDelete("teams/{teamId}/permissions/{permissionId}", TypedHandler)
                    .WithName("removeTeamPermissionFromTeam")
                    .WithDescription("Removes the specified TeamPermission from the specified Team.")
                    .WithSummary("Removes a Team Permission from a Team.")
            ];
        }

        async Task<Ok> TypedHandler(Guid teamId, Guid permissionId, IMediator mediator, CancellationToken cancellationToken)
        {
            await mediator.Send(new Command { TeamId = teamId, TeamPermissionId = permissionId }, cancellationToken);
            return TypedResults.Ok();
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.TeamId, [SystemPermission.ManageRoles], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var team = await db.Teams
                .Where(r => r.Id == request.TeamId)
                .SingleOrDefaultAsync(cancellationToken);

            var permission = await db.TeamPermissions
                .Where(p => p.Id == request.TeamPermissionId)
                .SingleOrDefaultAsync(cancellationToken);

            if (team == null)
                throw new EntityNotFoundException<Team>();

            if (permission == null)
                throw new EntityNotFoundException<TeamPermissionModel>();

            var teamPermission = await db.TeamPermissionAssignments
                .Where(x => x.TeamId == request.TeamId && x.PermissionId == request.TeamPermissionId)
                .SingleOrDefaultAsync(cancellationToken);

            if (teamPermission != null)
            {
                db.TeamPermissionAssignments.Remove(teamPermission);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
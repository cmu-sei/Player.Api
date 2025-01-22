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
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;

namespace Player.Api.Features.TeamPermissions;

public class GetMine
{
    [DataContract(Name = "GetMyTeamPermissionsQuery")]
    public class Query : IRequest<TeamPermissionsClaim[]>
    {
        public Guid? ViewId { get; set; }
        public Guid? TeamId { get; set; }
        public bool? IncludeAllViewTeams { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("team-permissions/mine", TypedHandler)
                    .WithName("getMyTeamPermissions")
                    .WithDescription(
                        @"Returns all TeamPermissions for the current user or those of a specified Team or View.
                        If a Team is specified, can optionally include TeamPermissions for all Teams in the same View that the User is a member of.")
                    .WithSummary("Gets Team Permissions for the current User.")
            ];
        }

        async Task<Ok<TeamPermissionsClaim[]>> TypedHandler([AsParameters] Query query, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(query, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db) : BaseHandler<Query, TeamPermissionsClaim[]>
    {
        // Anyone can access since it only returns results for the current User
        public override Task<bool> Authorize(Query request, CancellationToken cancellationToken) => Task.FromResult(true);

        public override async Task<TeamPermissionsClaim[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var permissions = authorizationService.GetTeamPermissions();

            if (request.ViewId.HasValue)
            {
                permissions = permissions.Where(x => x.ViewId == request.ViewId.Value);
            }
            else if (request.TeamId.HasValue)
            {
                var teamPermissions = permissions.Where(x => x.TeamId == request.TeamId.Value).FirstOrDefault();

                if (request.IncludeAllViewTeams.HasValue && request.IncludeAllViewTeams.Value)
                {
                    Guid? viewId = null;
                    if (teamPermissions == null)
                    {
                        viewId = await db.Teams
                            .Where(x => x.Id == request.TeamId.Value)
                            .Select(x => x.ViewId)
                            .FirstOrDefaultAsync(cancellationToken);
                    }
                    else
                    {
                        viewId = teamPermissions.ViewId;
                    }

                    permissions = permissions.Where(x => x.ViewId == viewId);
                }
                else
                {
                    permissions = permissions.Where(x => x.TeamId == request.TeamId.Value);
                }
            }

            return permissions.ToArray();
        }
    }
}
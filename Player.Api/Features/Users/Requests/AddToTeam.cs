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
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Teams;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;

namespace Player.Api.Features.Users;

public class AddToTeam
{
    [DataContract(Name = "AddUserToTeamCommand")]
    public record Command : IRequest
    {
        public Guid TeamId { get; set; }
        public Guid UserId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("teams/{teamId}/users/{userId}", TypedHandler)
                    .WithName("addUserToTeam")
                    .WithDescription("Adds the specified User to the specified Team.")
                    .WithSummary("Adds a User to a Team.")
            ];
        }

        async Task<Ok> TypedHandler(Guid teamId, Guid userId, IMediator mediator, CancellationToken cancellationToken)
        {
            await mediator.Send(new Command { TeamId = teamId, UserId = userId }, cancellationToken);
            return TypedResults.Ok();
        }
    }

    public class Handler(
        ILogger<AddToTeam> logger,
        IIdentityResolver identityResolver,
        IUserClaimsService claimsService,
        IPlayerAuthorizationService authorizationService,
        PlayerContext db) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.TeamId, [SystemPermission.ManageViews], [ViewPermission.ManageView], [TeamPermission.ManageTeam], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var team = await db.Teams
                .Where(t => t.Id == request.TeamId)
                .FirstOrDefaultAsync(cancellationToken);

            if (team == null)
                throw new EntityNotFoundException<Team>();

            var userExists = await db.Users
                .Where(u => u.Id == request.UserId)
                .AnyAsync(cancellationToken);

            if (!userExists)
                throw new EntityNotFoundException<User>();

            var viewIdQuery = db.Teams
                .Where(t => t.Id == request.TeamId)
                .Select(t => t.ViewId);

            var viewMembership = await db.ViewMemberships
                .Where(x => x.UserId == request.UserId && viewIdQuery.Contains(x.ViewId))
                .SingleOrDefaultAsync(cancellationToken);

            bool setPrimary = false;
            if (viewMembership == null)
            {
                viewMembership = new ViewMembershipEntity { ViewId = team.ViewId, UserId = request.UserId };
                db.ViewMemberships.Add(viewMembership);
                await db.SaveChangesAsync(cancellationToken);
                setPrimary = true;
            }

            var teamMembership = new TeamMembershipEntity
            {
                ViewMembershipId = viewMembership.Id,
                UserId = request.UserId,
                TeamId = request.TeamId
            };

            if (setPrimary)
            {
                viewMembership.PrimaryTeamMembership = teamMembership;
            }

            db.TeamMemberships.Add(teamMembership);

            await db.SaveChangesAsync(cancellationToken);
            await claimsService.RefreshClaims(request.UserId);
            logger.LogWarning($"User {request.UserId} added to team {request.TeamId} by {identityResolver.GetId()}");
        }
    }
}
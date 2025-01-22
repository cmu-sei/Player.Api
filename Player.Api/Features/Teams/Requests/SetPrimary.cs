using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;
using Player.Api.ViewModels;

namespace Player.Api.Features.Teams;

public class SetPrimary
{
    [DataContract(Name = "SetPrimaryTeamCommand")]
    public record Command : IRequest<Team>
    {
        [JsonIgnore]
        public Guid UserId { get; set; }

        [JsonIgnore]
        public Guid TeamId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("users/{userId}/teams/{teamId}/primary", TypedHandler)
                    .WithName("setUserPrimaryTeam")
                    .WithDescription("Sets the specified Team as a Primary Team for the specified User.")
                    .WithSummary("Sets a User's Primary Team.")
            ];
        }

        async Task<Ok<Team>> TypedHandler(Guid userId, Guid teamId, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Command { UserId = userId, TeamId = teamId }, cancellationToken));
        }
    }

    public class Handler(IUserClaimsService claimsService, IIdentityResolver identityResolver, PlayerContext db, IMapper mapper) : BaseHandler<Command, Team>
    {
        public override Task<bool> Authorize(Command request, CancellationToken cancellationToken)
        {
            if (identityResolver.GetId() == request.UserId)
            {
                return Task.FromResult(true);
            }
            else
            {
                throw new ForbiddenException("You can only change your own Primary Team.");
            }
        }

        public override async Task<Team> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var teamEntity = await db.Teams.SingleOrDefaultAsync(t => t.Id == request.TeamId, cancellationToken);

            var viewMembership = await db.ViewMemberships
                .Include(x => x.TeamMemberships)
                .SingleOrDefaultAsync(
                    x => x.ViewId == teamEntity.ViewId && x.UserId == request.UserId,
                    cancellationToken);

            var teamMembership = viewMembership.TeamMemberships
                .Where(x => x.TeamId == request.TeamId)
                .FirstOrDefault();

            if (teamMembership == null)
                throw new ConflictException("You can only change your Primary Team to a Team that you are a member of");

            viewMembership.PrimaryTeamMembershipId = teamMembership.Id;
            await db.SaveChangesAsync(cancellationToken);

            await claimsService.RefreshClaims(request.UserId);

            var team = await db.Teams
                .ProjectTo<TeamDTO>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == request.TeamId, cancellationToken);
            return mapper.Map<Team>(team);
        }
    }
}
using System;
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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.TeamMemberships;

public class Edit
{
    [DataContract(Name = "EditTeamMembershipCommand")]
    public record Command : IRequest<TeamMembership>
    {
        [JsonIgnore]
        public Guid Id { get; set; }
        public Guid? RoleId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPut("team-memberships/{id}", TypedHandler)
                    .WithName("updateTeamMembership")
                    .WithDescription("Updates a Team Membership with the attributes specified.")
                    .WithSummary("Updates a Team Membership.")
            ];
        }

        async Task<Ok<TeamMembership>> TypedHandler([FromRoute] Guid id, [FromBody] Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.Id = id;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(ILogger<Edit> logger, IIdentityResolver identityResolver, IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, TeamMembership>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamMembershipEntity>(request.Id, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<TeamMembership> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var membershipToUpdate = await db.TeamMemberships
                .Include(m => m.ViewMembership)
                .SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (membershipToUpdate == null)
                throw new EntityNotFoundException<TeamMembership>();

            mapper.Map(request, membershipToUpdate);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning($"Team Membership updated by {identityResolver.GetId()} = User: {membershipToUpdate.UserId}, Role: {membershipToUpdate.RoleId}, Team: {membershipToUpdate.TeamId}, ViewMembership: {membershipToUpdate.ViewMembershipId}");

            return await db.TeamMemberships
                .ProjectTo<TeamMembership>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == membershipToUpdate.Id, cancellationToken);
        }
    }
}
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
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Views;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Options;

namespace Player.Api.Features.Teams;

public class Create
{
    [DataContract(Name = "CreateTeamCommand")]
    public record Command : IRequest<Team>
    {
        [JsonIgnore]
        public Guid ViewId { get; set; }
        public string Name { get; set; }
        public Guid? RoleId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("views/{id}/teams", TypedHandler)
                    .WithName("createTeam")
                    .WithDescription("Creates a new Team within a View with the attributes specified.")
                    .WithSummary("Creates a new Team within a View.")
            ];
        }

        async Task<CreatedAtRoute<Team>> TypedHandler(Guid id, Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.ViewId = id;
            var created = await mediator.Send(command, cancellationToken);
            return TypedResults.CreatedAtRoute(created, "getTeam", new { id = created.Id });
        }
    }

    public class Handler(ILogger<Create> logger,
                         IIdentityResolver identityResolver,
                         IPlayerAuthorizationService authorizationService,
                         PlayerContext db,
                         IMapper mapper,
                         RoleOptions roleOptions) : BaseHandler<Command, Team>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ViewEntity>(request.ViewId, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<Team> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var viewEntity = await db.Views
                .SingleOrDefaultAsync(e => e.Id == request.ViewId, cancellationToken);

            if (viewEntity == null)
                throw new EntityNotFoundException<View>();

            var teamEntity = mapper.Map<TeamEntity>(request);

            if (!request.RoleId.HasValue)
            {
                var defaultRoleId = await db.TeamRoles
                    .Where(x => x.Name == roleOptions.DefaultTeamRole)
                    .Select(x => x.Id)
                    .SingleAsync(cancellationToken);

                teamEntity.RoleId = defaultRoleId;
            }

            viewEntity.Teams.Add(teamEntity);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning($"Team {teamEntity.Name} ({teamEntity.Id}) in View {teamEntity.ViewId} created by {identityResolver.GetId()}");

            var team = await db.Teams
                .ProjectTo<TeamDTO>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == teamEntity.Id, cancellationToken);

            return mapper.Map<Team>(team);
        }
    }
}
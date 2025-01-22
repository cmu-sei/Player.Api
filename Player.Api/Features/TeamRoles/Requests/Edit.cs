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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Options;

namespace Player.Api.Features.TeamRoles;

public class Edit
{
    [DataContract(Name = "EditTeamRoleCommand")]
    public record Command : Create.Command
    {
        [JsonIgnore]
        public Guid Id { get; set; }
        public bool AllPermissions { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPut("team-roles/{id}", TypedHandler)
                    .WithName("updateTeamRole")
                    .WithDescription("Updates a Team Role with the attributes specified.")
                    .WithSummary("Updates a Team Role.")
            ];
        }

        async Task<Ok<TeamRole>> TypedHandler([FromRoute] Guid id, [FromBody] Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.Id = id;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper, RoleOptions roleOptions) : BaseHandler<Command, TeamRole>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageRoles], [], [], cancellationToken);

        public override async Task<TeamRole> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var roleToUpdate = await db.TeamRoles.SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (roleToUpdate == null)
                throw new EntityNotFoundException<TeamRole>();

            if (roleToUpdate.Name != request.Name)
            {
                var defaultRoleIds = await db.TeamRoles
                .Where(x => x.Name == roleOptions.DefaultTeamRole || x.Name == roleOptions.DefaultViewCreatorRole)
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken);

                if (defaultRoleIds.Contains(roleToUpdate.Id))
                    throw new ConflictException(
                        $"Cannot change the {nameof(request.Name)} of {nameof(roleOptions.DefaultTeamRole)} ({roleOptions.DefaultTeamRole}) " +
                        $"or {nameof(roleOptions.DefaultViewCreatorRole)} ({roleOptions.DefaultViewCreatorRole})");
            }

            mapper.Map(request, roleToUpdate);
            await db.SaveChangesAsync(cancellationToken);

            return await db.TeamRoles
                .ProjectTo<TeamRole>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == request.Id, cancellationToken);
        }
    }
}
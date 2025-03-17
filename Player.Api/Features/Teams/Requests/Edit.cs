// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

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
using Player.Api.ViewModels;

namespace Player.Api.Features.Teams;

public class Edit
{
    [DataContract(Name = "EditTeamCommand")]
    public record Command : IRequest<Team>
    {
        [JsonIgnore]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid? RoleId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPut("teams/{id}", TypedHandler)
                    .WithName("updateTeam")
                    .WithDescription("Updates a Team with the attributes specified.")
                    .WithSummary("Updates a Team.")
            ];
        }

        async Task<Ok<Team>> TypedHandler([FromRoute] Guid id, [FromBody] Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.Id = id;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(ILogger<Edit> logger, IIdentityResolver identityResolver, IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, Team>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.Id, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<Team> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var teamToUpdate = await db.Teams
                .SingleOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

            if (teamToUpdate == null)
                throw new EntityNotFoundException<Team>();

            var newRole = request.RoleId == teamToUpdate.RoleId ? "" : request.RoleId.ToString();
            mapper.Map(request, teamToUpdate);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning($"Team {teamToUpdate.Name} ({teamToUpdate.Id}) in View {teamToUpdate.ViewId} updated by {identityResolver.GetId()} added Role: {newRole}");

            var team = await db.Teams
                .ProjectTo<TeamDTO>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == teamToUpdate.Id, cancellationToken);

            return mapper.Map<Team>(team);
        }
    }
}
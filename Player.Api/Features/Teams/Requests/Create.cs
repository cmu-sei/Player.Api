// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

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
using Npgsql;
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
            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("Team Name is required and cannot be empty.");

            if (request.ViewId == Guid.Empty)
                throw new ArgumentException("ViewId is required and cannot be empty.");

            // Validate ViewId exists
            var viewEntity = await db.Views
                .SingleOrDefaultAsync(e => e.Id == request.ViewId, cancellationToken);

            if (viewEntity == null)
                throw new EntityNotFoundException<View>($"Invalid ViewId '{request.ViewId}'. The View does not exist.");

            // Validate RoleId if provided
            if (request.RoleId.HasValue && request.RoleId.Value != Guid.Empty)
            {
                var roleExists = await db.TeamRoles.AnyAsync(r => r.Id == request.RoleId.Value, cancellationToken);
                if (!roleExists)
                    throw new ArgumentException($"Invalid RoleId '{request.RoleId.Value}'. The TeamRole does not exist.");
            }

            var teamEntity = mapper.Map<TeamEntity>(request);

            if (!request.RoleId.HasValue)
            {
                var defaultRoleId = await db.TeamRoles
                    .Where(x => x.Name == roleOptions.DefaultTeamRole)
                    .Select(x => x.Id)
                    .SingleAsync(cancellationToken);

                teamEntity.RoleId = defaultRoleId;
            }

            try
            {
                viewEntity.Teams.Add(teamEntity);
                await db.SaveChangesAsync(cancellationToken);

                var team = await db.Teams
                    .ProjectTo<TeamDTO>(mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(o => o.Id == teamEntity.Id, cancellationToken);

                return mapper.Map<Team>(team);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx)
            {
                // Handle specific PostgreSQL errors
                switch (pgEx.SqlState)
                {
                    case "23505": // unique_violation
                        throw new InvalidOperationException($"A Team with the ID '{teamEntity.Id}' already exists.", ex);
                    case "23503": // foreign_key_violation
                        var constraintName = pgEx.ConstraintName ?? "unknown";
                        if (constraintName.Contains("ViewId", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"Invalid ViewId '{request.ViewId}'. The View does not exist.", ex);
                        }
                        if (constraintName.Contains("RoleId", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"Invalid RoleId '{request.RoleId}'. The TeamRole does not exist.", ex);
                        }
                        throw new InvalidOperationException($"Foreign key constraint violated: {constraintName}. Please verify all referenced entities exist.", ex);
                    case "23514": // check_violation
                        throw new InvalidOperationException($"Data validation failed: {pgEx.MessageText}", ex);
                    default:
                        throw new InvalidOperationException($"Database error creating Team: {pgEx.MessageText}", ex);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"An unexpected error occurred while creating the Team: {ex.Message}", ex);
            }
        }
    }
}
// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
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
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Options;

namespace Player.Api.Features.Views;

public class Create
{
    [DataContract(Name = "CreateViewCommand")]
    public record Command : IRequest<View>
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public ViewStatus Status { get; set; }
        public bool CreateAdminTeam { get; set; } = true;
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("views", TypedHandler)
                    .WithName("createView")
                    .WithDescription("Creates a new View with the attributes specified.")
                    .WithSummary("Creates a new View.")
            ];
        }

        async Task<CreatedAtRoute<View>> TypedHandler(Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            var created = await mediator.Send(command, cancellationToken);
            return TypedResults.CreatedAtRoute(created, "getView", new { id = created.Id });
        }
    }

    public class Handler(IIdentityResolver identityResolver,
                         IPlayerAuthorizationService authorizationService,
                         PlayerContext db,
                         IMapper mapper,
                         RoleOptions roleOptions,
                         ILogger<Handler> logger) : BaseHandler<Command, View>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.CreateViews], [], [], cancellationToken);

        public override async Task<View> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("View Name is required and cannot be empty.");

            var viewEntity = mapper.Map<ViewEntity>(request);

            var viewAdminRole = await db.TeamRoles
                .Where(p => p.Name == roleOptions.DefaultViewCreatorRole)
                .SingleAsync(cancellationToken);

            var userId = identityResolver.GetId();

            TeamEntity teamEntity = null;
            ViewMembershipEntity viewMembershipEntity = null;

            // Create an Admin team with the caller as a member
            if (request.CreateAdminTeam)
            {
                teamEntity = new TeamEntity() { Name = "Admin", RoleId = viewAdminRole.Id };
                viewMembershipEntity = new ViewMembershipEntity { View = viewEntity, UserId = userId };
                viewEntity.Teams.Add(teamEntity);
                viewEntity.Memberships.Add(viewMembershipEntity);

            }

            try
            {
                db.Views.Add(viewEntity);
                await db.SaveChangesAsync(cancellationToken);

                if (request.CreateAdminTeam)
                {
                    var teamMembershipEntity = new TeamMembershipEntity { Team = teamEntity, UserId = userId, ViewMembership = viewMembershipEntity };
                    viewMembershipEntity.PrimaryTeamMembership = teamMembershipEntity;
                    db.TeamMemberships.Add(teamMembershipEntity);
                    db.ViewMemberships.Update(viewMembershipEntity);
                    await db.SaveChangesAsync(cancellationToken);
                }

                await db.SaveChangesAsync(cancellationToken);

                var item = await db.Views
                    .SingleOrDefaultAsync(o => o.Id == viewEntity.Id, cancellationToken);

                return mapper.Map<View>(item);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx)
            {
                // Handle specific PostgreSQL errors
                switch (pgEx.SqlState)
                {
                    case "23505": // unique_violation
                        throw new InvalidOperationException($"A View with the name '{request.Name}' already exists.", ex);
                    case "23503": // foreign_key_violation
                        var constraintName = pgEx.ConstraintName ?? "unknown";
                        throw new InvalidOperationException($"Foreign key constraint violated: {constraintName}. Please verify all referenced entities exist.", ex);
                    case "23514": // check_violation
                        throw new InvalidOperationException($"Data validation failed: {pgEx.MessageText}", ex);
                    default:
                        throw new InvalidOperationException($"Database error creating View: {pgEx.MessageText}", ex);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"An unexpected error occurred while creating the View: {ex.Message}", ex);
            }
        }
    }
}
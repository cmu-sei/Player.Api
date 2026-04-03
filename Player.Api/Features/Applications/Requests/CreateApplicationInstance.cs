// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.Serialization;
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
using Player.Api.Features.Teams;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Features.Applications;

public class CreateApplicationInstance
{
    [DataContract(Name = "CreateApplicationInstanceCommand")]
    public class Command : IRequest<ApplicationInstance>
    {
        [Required]
        public Guid TeamId { get; set; }

        [Required]
        public Guid ApplicationId { get; set; }

        public float DisplayOrder { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("teams/{id}/application-instances", TypedHandler)
                    .WithName("createApplicationInstance")
                    .WithDescription("Creates a new Application Instance within a Team with the attributes specified")
                    .WithSummary("Creates a new Application Instance within a Team.")
            ];
        }

        async Task<CreatedAtRoute<ApplicationInstance>> TypedHandler(Guid id, Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.TeamId = id;
            var createdApplicationInstance = await mediator.Send(command, cancellationToken);
            return TypedResults.CreatedAtRoute(createdApplicationInstance, "getApplicationInstance", new { id = createdApplicationInstance.Id });
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper, ILogger<Handler> logger) : BaseHandler<Command, ApplicationInstance>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.TeamId, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<ApplicationInstance> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            // Validate required fields
            if (request.TeamId == Guid.Empty)
            {
                logger.LogWarning("CreateApplicationInstance failed: TeamId is required");
                throw new ArgumentException("TeamId is required and cannot be empty.");
            }

            if (request.ApplicationId == Guid.Empty)
            {
                logger.LogWarning("CreateApplicationInstance failed: ApplicationId is required");
                throw new ArgumentException("ApplicationId is required and cannot be empty.");
            }

            var team = await db.Teams.Where(e => e.Id == request.TeamId).SingleOrDefaultAsync(cancellationToken);

            if (team == null)
            {
                logger.LogWarning($"CreateApplicationInstance failed: Team {request.TeamId} not found");
                throw new EntityNotFoundException<Team>($"Invalid TeamId '{request.TeamId}'. The Team does not exist.");
            }

            // Validate ApplicationId exists
            var applicationExists = await db.Applications.AnyAsync(a => a.Id == request.ApplicationId, cancellationToken);
            if (!applicationExists)
            {
                logger.LogWarning($"CreateApplicationInstance failed: Application {request.ApplicationId} not found");
                throw new ArgumentException($"Invalid ApplicationId '{request.ApplicationId}'. The Application does not exist.");
            }

            var instanceEntity = mapper.Map<ApplicationInstanceEntity>(request);

            try
            {
                db.ApplicationInstances.Add(instanceEntity);
                await db.SaveChangesAsync(cancellationToken);


                var instance = await db.ApplicationInstances
                    .ProjectTo<ApplicationInstance>(mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(i => i.Id == instanceEntity.Id, cancellationToken);

                return instance;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx)
            {

                // Handle specific PostgreSQL errors
                switch (pgEx.SqlState)
                {
                    case "23505": // unique_violation
                        throw new InvalidOperationException($"An ApplicationInstance for Application '{request.ApplicationId}' and Team '{request.TeamId}' already exists.", ex);
                    case "23503": // foreign_key_violation
                        var constraintName = pgEx.ConstraintName ?? "unknown";
                        if (constraintName.Contains("TeamId", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"Invalid TeamId '{request.TeamId}'. The Team does not exist.", ex);
                        }
                        if (constraintName.Contains("ApplicationId", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"Invalid ApplicationId '{request.ApplicationId}'. The Application does not exist.", ex);
                        }
                        throw new InvalidOperationException($"Foreign key constraint violated: {constraintName}. Please verify all referenced entities exist.", ex);
                    case "23514": // check_violation
                        throw new InvalidOperationException($"Data validation failed: {pgEx.MessageText}", ex);
                    default:
                        throw new InvalidOperationException($"Database error creating ApplicationInstance: {pgEx.MessageText}", ex);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"An unexpected error occurred while creating the ApplicationInstance: {ex.Message}", ex);
            }
        }
    }
}
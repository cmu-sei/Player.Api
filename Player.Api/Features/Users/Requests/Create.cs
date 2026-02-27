// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
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
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;

namespace Player.Api.Features.Users;

public class Create
{
    [DataContract(Name = "CreateUserCommand")]
    public record Command : IRequest<User>
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid? RoleId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("users", TypedHandler)
                    .WithName("createUser")
                    .WithDescription("Creates a new User with the attributes specified.")
                    .WithSummary("Creates a new User.")
            ];
        }

        async Task<CreatedAtRoute<User>> TypedHandler(Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            var created = await mediator.Send(command, cancellationToken);
            return TypedResults.CreatedAtRoute(created, "getUser", new { id = created.Id });
        }
    }

    public class Handler(ILogger<Create> logger, IIdentityResolver identityResolver, IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, User>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageUsers], [], [], cancellationToken);

        public override async Task<User> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                logger.LogWarning("CreateUser failed: Name is required");
                throw new ArgumentException("User Name is required and cannot be empty.");
            }

            if (request.Id == Guid.Empty)
            {
                logger.LogWarning("CreateUser failed: Id is required");
                throw new ArgumentException("User Id is required and cannot be empty.");
            }

            var userEntity = mapper.Map<UserEntity>(request);

            try
            {
                db.Users.Add(userEntity);
                await db.SaveChangesAsync(cancellationToken);


                return await db.Users
                    .ProjectTo<User>(mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(o => o.Id == userEntity.Id, cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx)
            {

                // Handle specific PostgreSQL errors
                switch (pgEx.SqlState)
                {
                    case "23505": // unique_violation
                        throw new InvalidOperationException($"A User with the ID '{request.Id}' already exists.", ex);
                    case "23503": // foreign_key_violation
                        var constraintName = pgEx.ConstraintName ?? "unknown";
                        throw new InvalidOperationException($"Foreign key constraint violated: {constraintName}. Please verify all referenced entities exist.", ex);
                    case "23514": // check_violation
                        throw new InvalidOperationException($"Data validation failed: {pgEx.MessageText}", ex);
                    default:
                        throw new InvalidOperationException($"Database error creating User: {pgEx.MessageText}", ex);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"An unexpected error occurred while creating the User: {ex.Message}", ex);
            }
        }
    }
}
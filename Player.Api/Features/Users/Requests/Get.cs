// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
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
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using TeamPermission = Player.Api.Data.Data.Models.TeamPermission;

namespace Player.Api.Features.Users;

public class Get
{
    [DataContract(Name = "GetUserQuery")]
    public class Query : IRequest<User>
    {
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("users/{id}", TypedHandler)
                    .WithName("getUser")
                    .WithDescription("Returns the User with the id specified.")
                    .WithSummary("Gets a specific User by id.")
            ];
        }

        async Task<Ok<User>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { Id = id }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, User>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken)
        {
            if (await authorizationService.Authorize([SystemPermission.ViewUsers], [ViewPermission.ViewView], [TeamPermission.ManageTeam], cancellationToken))
            {
                return true;
            }
            else
            {
                // Allow if current User has access to any of the Teams the target User is a member of.
                var teams = await db.TeamMemberships
                    .Where(x => x.UserId == request.Id)
                    .Select(x => x.Team)
                    .ToArrayAsync(cancellationToken);

                foreach (var team in teams)
                {
                    if (await authorizationService.Authorize<TeamEntity>(team.Id, [], [], [TeamPermission.ViewTeam], cancellationToken))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public override async Task<User> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var item = await db.Users
                .ProjectTo<User>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

            if (item == null)
                throw new EntityNotFoundException<User>();

            return mapper.Map<User>(item);
        }
    }
}
// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

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
using Player.Api.Options;
using TeamPermission = Player.Api.Data.Data.Models.TeamPermission;

namespace Player.Api.Features.Users;

public class GetAll
{
    [DataContract(Name = "GetUsersQuery")]
    public record Query : IRequest<User[]>
    {

    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("users", TypedHandler)
                    .WithName("getUsers")
                    .WithDescription("Returns all Users in the system. Includes configured identity attributes for callers with the ViewUsers permission.")
                    .WithSummary("Gets all Users in the system.")
            ];
        }

        async Task<Ok<User[]>> TypedHandler(IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query(), cancellationToken));
        }
    }

    public class Handler(
        IPlayerAuthorizationService authorizationService,
        ClaimsTransformationOptions claimsOptions,
        PlayerContext db,
        IMapper mapper) : BaseHandler<Query, User[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewUsers], [ViewPermission.ManageView], [TeamPermission.ManageTeam], cancellationToken);

        public override async Task<User[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var users = await db.Users
                .ProjectTo<User>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);

            var canViewIdentityAttributes = await authorizationService.Authorize(
                [SystemPermission.ViewUsers],
                cancellationToken);

            if (!canViewIdentityAttributes)
            {
                return users;
            }

            var definitions = claimsOptions.GetUserAttributeDefinitions();
            var definitionKeys = definitions
                .Select(definition => definition.Key)
                .ToArray();
            var values = await db.UserIdentityAttributes
                .AsNoTracking()
                .Where(attribute => definitionKeys.Contains(attribute.Key))
                .ToArrayAsync(cancellationToken);
            var valuesByUserAndKey = values.ToDictionary(
                attribute => (attribute.UserId, attribute.Key),
                attribute => attribute.Value);

            foreach (var user in users)
            {
                user.IdentityAttributes = definitions
                    .Select(definition =>
                    {
                        valuesByUserAndKey.TryGetValue((user.Id, definition.Key), out var value);

                        return new UserIdentityAttribute
                        {
                            Key = definition.Key,
                            Name = definition.Name,
                            Value = value
                        };
                    })
                    .ToArray();
            }

            return users;
        }
    }
}

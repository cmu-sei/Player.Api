// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
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

namespace Player.Api.Features.Users;

public class GetAdminUsers
{
    [DataContract(Name = "GetAdminUsersQuery")]
    public record Query : IRequest<AdminUsers>;

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("admin/users", TypedHandler)
                    .WithName("getAdminUsers")
                    .WithDescription("Returns Users and configured identity attributes for system administration.")
                    .WithSummary("Gets Users for system administration.")
            ];
        }

        async Task<Ok<AdminUsers>> TypedHandler(IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query(), cancellationToken));
        }
    }

    public class Handler(
        IPlayerAuthorizationService authorizationService,
        ClaimsTransformationOptions claimsOptions,
        PlayerContext db) : BaseHandler<Query, AdminUsers>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewUsers], [], [], cancellationToken);

        public override async Task<AdminUsers> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var definitions = claimsOptions.GetUserAttributeDefinitions();
            var users = await db.Users
                .AsNoTracking()
                .Include(user => user.Role)
                .Include(user => user.IdentityAttributes)
                .ToArrayAsync(cancellationToken);

            return new AdminUsers
            {
                AttributeDefinitions = definitions
                    .Select(definition => new UserIdentityAttributeDefinition
                    {
                        Key = definition.Key,
                        Name = definition.Name
                    })
                    .ToArray(),
                Users = users
                    .Select(user =>
                    {
                        var valuesByKey = user.IdentityAttributes
                            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);

                        return new AdminUser
                        {
                            Id = user.Id,
                            Name = user.Name,
                            RoleId = user.RoleId,
                            RoleName = user.RoleId.HasValue ? user.Role.Name : null,
                            IdentityAttributes = definitions
                                .Select(definition =>
                                {
                                    valuesByKey.TryGetValue(definition.Key, out var value);

                                    return new UserIdentityAttribute
                                    {
                                        Key = definition.Key,
                                        Value = value
                                    };
                                })
                                .ToArray()
                        };
                    })
                    .ToArray()
            };
        }
    }
}

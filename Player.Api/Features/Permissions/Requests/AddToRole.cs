using System;
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
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Roles;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;

namespace Player.Api.Features.Permissions;

public class AddToRole
{
    [DataContract(Name = "AddPermissionToRoleCommand")]
    public record Command : IRequest
    {
        public Guid RoleId { get; set; }
        public Guid PermissionId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("roles/{roleId}/permissions/{permissionId}", TypedHandler)
                    .WithName("addPermissionToRole")
                    .WithDescription("Adds the specified Permission to the specified Role.")
                    .WithSummary("Adds a Permission to a Role.")
            ];
        }

        async Task<Ok> TypedHandler(Guid roleId, Guid permissionId, IMediator mediator, CancellationToken cancellationToken)
        {
            await mediator.Send(new Command { RoleId = roleId, PermissionId = permissionId }, cancellationToken);
            return TypedResults.Ok();
        }
    }

    public class Handler(ILogger<AddToRole> logger, IIdentityResolver identityResolver, IPlayerAuthorizationService authorizationService, PlayerContext db) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageRoles], [], [], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var role = await db.Roles
                .Where(r => r.Id == request.RoleId)
                .SingleOrDefaultAsync();

            var permission = await db.Permissions
                .Where(p => p.Id == request.PermissionId)
                .SingleOrDefaultAsync();

            if (role == null)
                throw new EntityNotFoundException<Role>();

            if (permission == null)
                throw new EntityNotFoundException<Permission>();

            role.Permissions.Add(new RolePermissionEntity(request.RoleId, request.PermissionId));

            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning($"Permission {permission.Name} ({request.PermissionId}) added to Role {role.Name} ({request.RoleId}) by {identityResolver.GetId()}");
        }
    }
}
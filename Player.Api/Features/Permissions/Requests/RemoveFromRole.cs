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

public class RemoveFromRole
{
    [DataContract(Name = "RemovePermissionFromRoleCommand")]
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
                group.MapDelete("roles/{roleId}/permissions/{permissionId}", TypedHandler)
                    .WithName("removePermissionFromRole")
                    .WithDescription("Removes the specified Permission from the specified Role.")
                    .WithSummary("Removes a Permission from a Role.")
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
                .SingleOrDefaultAsync(cancellationToken);

            if (role == null)
                throw new EntityNotFoundException<Role>();

            var permission = await db.Permissions
                .Where(p => p.Id == request.PermissionId)
                .SingleOrDefaultAsync(cancellationToken);

            if (permission == null)
                throw new EntityNotFoundException<Permission>();

            var rolePermission = await db.RolePermissions
                .Where(x => x.RoleId == request.RoleId && x.PermissionId == request.PermissionId)
                .SingleOrDefaultAsync(cancellationToken);

            if (rolePermission != null)
            {
                db.RolePermissions.Remove(rolePermission);
                await db.SaveChangesAsync(cancellationToken);
            }

            logger.LogWarning($"Permission {permission.Name} ({request.PermissionId}) removed from Role {role.Name} ({request.RoleId}) by {identityResolver.GetId()}");
        }
    }
}
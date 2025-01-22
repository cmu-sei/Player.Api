using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.ViewModels;

namespace Player.Api.Features.Permissions;

public class Create
{
    [DataContract(Name = "CreatePermissionCommand")]
    public record Command : IRequest<Permission>
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("permissions", TypedHandler)
                    .WithName("createPermission")
                    .WithDescription("Creates a new Permission with the attributes specified.")
                    .WithSummary("Creates a new Permission.")
            ];
        }

        async Task<CreatedAtRoute<Permission>> TypedHandler(Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            var created = await mediator.Send(command, cancellationToken);
            return TypedResults.CreatedAtRoute(created, "getPermission", new { id = created.Id });
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, Permission>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageRoles], [], [], cancellationToken);

        public override async Task<Permission> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var permissionEntity = mapper.Map<PermissionEntity>(request);

            db.Permissions.Add(permissionEntity);
            await db.SaveChangesAsync(cancellationToken);

            return mapper.Map<Permission>(permissionEntity);
        }
    }
}
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

namespace Player.Api.Features.Roles;

public class Create
{
    [DataContract(Name = "CreateRoleCommand")]
    public record Command : IRequest<Role>
    {
        public string Name { get; set; }
        public bool AllPermissions { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("roles", TypedHandler)
                    .WithName("createRole")
                    .WithDescription("Creates a new Role with the attributes specified.")
                    .WithSummary("Creates a new Role.")
            ];
        }

        async Task<CreatedAtRoute<Role>> TypedHandler(Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            var created = await mediator.Send(command, cancellationToken);
            return TypedResults.CreatedAtRoute(created, "getRole", new { id = created.Id });
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, Role>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageRoles], [], [], cancellationToken);

        public override async Task<Role> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            // Ensure role with this name does not already exist
            var role = await db.Roles
                .ProjectTo<Role>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Name == request.Name);

            if (role != null)
                throw new ConflictException("A role with that name already exists.");

            var roleEntity = mapper.Map<RoleEntity>(request);

            db.Roles.Add(roleEntity);
            await db.SaveChangesAsync(cancellationToken);

            return mapper.Map<Role>(roleEntity);
        }
    }
}
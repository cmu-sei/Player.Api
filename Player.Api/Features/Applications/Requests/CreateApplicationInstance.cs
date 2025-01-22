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

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, ApplicationInstance>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.TeamId, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<ApplicationInstance> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var team = await db.Teams.Where(e => e.Id == request.TeamId).SingleOrDefaultAsync(cancellationToken);

            if (team == null)
                throw new EntityNotFoundException<Team>();

            var instanceEntity = mapper.Map<ApplicationInstanceEntity>(request);

            db.ApplicationInstances.Add(instanceEntity);
            await db.SaveChangesAsync(cancellationToken);

            var instance = await db.ApplicationInstances
                .ProjectTo<ApplicationInstance>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(i => i.Id == instanceEntity.Id, cancellationToken);

            return instance;
        }
    }
}
using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;
using TeamPermission = Player.Api.Data.Data.Models.TeamPermission;

namespace Player.Api.Features.Applications;

public class EditApplicationInstance
{
    [DataContract(Name = "EditApplicationInstanceCommand")]
    public class Command : CreateApplicationInstance.Command
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPut("application-instances/{id}", TypedHandler)
                    .WithName("updateApplicationInstance")
                    .WithDescription("Updates an Application Instance with the attributes specified.")
                    .WithSummary("Updates an Application Instance.")
            ];
        }

        async Task<Ok<ApplicationInstance>> TypedHandler([FromRoute] Guid id, [FromBody] Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.Id = id;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, ApplicationInstance>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<TeamEntity>(request.TeamId, [SystemPermission.ManageViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<ApplicationInstance> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var instanceToUpdate = await db.ApplicationInstances
                .Include(ai => ai.Team)
                .SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (instanceToUpdate == null)
                throw new EntityNotFoundException<ApplicationInstance>();

            mapper.Map(request, instanceToUpdate);
            await db.SaveChangesAsync(cancellationToken);

            var instance = await db.ApplicationInstances
                .ProjectTo<ApplicationInstance>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(i => i.Id == instanceToUpdate.Id, cancellationToken);

            return instance;
        }
    }
}
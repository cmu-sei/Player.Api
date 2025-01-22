using System;
using System.Collections.Generic;
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

public class MoveApplicationInstance
{
    [DataContract(Name = "MoveApplicationInstanceCommand")]
    public record Command : IRequest<IEnumerable<ApplicationInstance>>
    {
        public Guid Id { get; set; }
        public Direction Direction { get; set; }
    }

    public enum Direction
    {
        Up,
        Down
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            var builder = group.MapPost("application-instances/{id}/move-up", MoveUpHandler)
                .WithName("moveUpApplicationInstance")
                .WithDescription("Moves an Application Instance up one spot in the list.")
                .WithSummary("Moves an Application Instance up.");

            var builder2 = group.MapPost("application-instances/{id}/move-down", MoveDownHandler)
                .WithName("moveDownApplicationInstance")
                .WithDescription("Moves an Application Instance down one spot in the list.")
                .WithSummary("Moves an Application Instance down.");

            return [builder, builder2];
        }

        async Task<Ok<IEnumerable<ApplicationInstance>>> MoveUpHandler([FromRoute] Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Command { Id = id, Direction = Direction.Up }, cancellationToken));
        }

        async Task<Ok<IEnumerable<ApplicationInstance>>> MoveDownHandler([FromRoute] Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Command { Id = id, Direction = Direction.Down }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, IEnumerable<ApplicationInstance>>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ApplicationInstanceEntity>(request.Id, [SystemPermission.ManageViews], [ViewPermission.ManageView], [TeamPermission.ManageTeam], cancellationToken);

        public override async Task<IEnumerable<ApplicationInstance>> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var instanceToUpdate = await db.ApplicationInstances
                .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);

            if (instanceToUpdate == null)
                throw new EntityNotFoundException<ApplicationInstance>();

            var teamInstances = await db.ApplicationInstances
                .Where(x => x.TeamId == instanceToUpdate.TeamId)
                .OrderBy(x => x.DisplayOrder)
                .ToArrayAsync(cancellationToken);

            if (request.Direction == Direction.Up)
            {
                for (int i = 0; i < teamInstances.Length; i++)
                {
                    var teamInstance = teamInstances[i];

                    if (teamInstance.Id == request.Id)
                    {
                        teamInstance.DisplayOrder = i - 1;

                        var previous = teamInstances.ElementAtOrDefault(i - 1);

                        if (previous != null)
                        {
                            previous.DisplayOrder = previous.DisplayOrder + 1;
                        }
                    }
                    else
                    {
                        teamInstance.DisplayOrder = i;
                    }
                }
            }
            else if (request.Direction == Direction.Down)
            {
                for (int i = teamInstances.Length - 1; i >= 0; i--)
                {
                    var teamInstance = teamInstances[i];

                    if (teamInstance.Id == request.Id)
                    {
                        teamInstance.DisplayOrder = i + 1;

                        var previous = teamInstances.ElementAtOrDefault(i + 1);

                        if (previous != null)
                        {
                            previous.DisplayOrder = previous.DisplayOrder - 1;
                        }
                    }
                    else
                    {
                        teamInstance.DisplayOrder = i;
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            var instances = await db.ApplicationInstances
                .Where(x => x.TeamId == instanceToUpdate.TeamId)
                .OrderBy(x => x.DisplayOrder)
                .ProjectTo<ApplicationInstance>(mapper.ConfigurationProvider)
                .ToArrayAsync(cancellationToken);

            return instances;
        }
    }
}
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
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Features.Views;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;

namespace Player.Api.Features.Applications;

public class GetByView
{
    [DataContract(Name = "GetApplicationsByViewQuery")]
    public class Query : IRequest<IEnumerable<Application>>
    {
        public Guid ViewId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("views/{viewId}/applications", TypedHandler)
                    .WithName("getViewApplications")
                    .WithDescription("Returns all Applications assigned to a specific View.")
                    .WithSummary("Gets all Applications for a View.")
            ];
        }

        async Task<Ok<IEnumerable<Application>>> TypedHandler(Guid viewId, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { ViewId = viewId }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, IEnumerable<Application>>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<ViewEntity>(request.ViewId, [SystemPermission.ViewViews], [ViewPermission.ViewView], [], cancellationToken);

        public override async Task<IEnumerable<Application>> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var view = await db.Views
                .Include(e => e.Applications)
                .Where(e => e.Id == request.ViewId)
                .SingleOrDefaultAsync(cancellationToken);

            if (view == null)
                throw new EntityNotFoundException<View>();

            return mapper.Map<IEnumerable<Application>>(view.Applications);
        }
    }
}
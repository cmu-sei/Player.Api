using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
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

namespace Player.Api.Features.Views;

public class Get
{
    [DataContract(Name = "GetViewQuery")]
    public class Query : IRequest<View>
    {
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("views/{id}", TypedHandler)
                    .WithName("getView")
                    .WithDescription("Returns the View with the id specified.")
                    .WithSummary("Gets a specific View by id.")
            ];
        }

        async Task<Ok<View>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { Id = id }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, View>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewViews], [], [], cancellationToken) ||
            authorizationService.GetAuthorizedViewIds().Contains(request.Id);

        public override async Task<View> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var item = await db.Views
                .SingleOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

            if (item == null)
                throw new EntityNotFoundException<View>();

            return mapper.Map<View>(item);
        }
    }
}
using System.Collections.Generic;
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
using Player.Api.ViewModels;

namespace Player.Api.Features.Applications;

public class GetAllTemplates
{
    [DataContract(Name = "GetAllApplicationTemplatesQuery")]
    public class Query : IRequest<IEnumerable<ApplicationTemplate>>
    {

    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("application-templates", TypedHandler)
                    .WithName("getApplicationTemplates")
                    .WithDescription("Returns a list of all of the Application Templates in the system.")
                    .WithSummary("Gets all Application Templates in the system.")
            ];
        }

        async Task<Ok<IEnumerable<ApplicationTemplate>>> TypedHandler(IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query(), cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, IEnumerable<ApplicationTemplate>>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewApplications, SystemPermission.ViewViews], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<IEnumerable<ApplicationTemplate>> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var items = await db.ApplicationTemplates
                .ProjectTo<ApplicationTemplate>(mapper.ConfigurationProvider)
                .ToListAsync(cancellationToken);

            return items;
        }
    }
}
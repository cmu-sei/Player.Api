using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
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
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Endpoints;
using Player.Api.ViewModels;

namespace Player.Api.Features.Applications;

public class GetApplicationTemplate
{
    [DataContract(Name = "GetApplicationTemplateQuery")]
    public class Query : IRequest<ApplicationTemplate>
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapGet("application-templates/{id}", TypedHandler)
                    .WithName("getApplicationTemplate")
                    .WithDescription("Returns the Application Template with the id specified.")
                    .WithSummary("Gets a specific Application Template by id.")
            ];
        }

        async Task<Ok<ApplicationTemplate>> TypedHandler(Guid id, IMediator mediator, CancellationToken cancellationToken)
        {
            return TypedResults.Ok(await mediator.Send(new Query { Id = id }, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Query, ApplicationTemplate>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ViewApplications], [ViewPermission.ManageView], [], cancellationToken);

        public override async Task<ApplicationTemplate> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var item = await db.ApplicationTemplates
                .ProjectTo<ApplicationTemplate>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

            return item;
        }
    }
}
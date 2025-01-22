using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

public class CreateApplicationTemplate
{
    [DataContract(Name = "CreateApplicationTemplateCommand")]
    public class Command : IRequest<ApplicationTemplate>
    {
        public string Name { get; set; }

        [Url]
        public string Url { get; set; }

        public string Icon { get; set; }

        public bool Embeddable { get; set; }
        public bool LoadInBackground { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("application-templates", TypedHandler)
                    .WithName("createApplicationTemplate")
                    .WithDescription("Creates a new Application Template with the attributes specified. An Application Template is a top-level resource that can optionally be the parent of an View specific Application resource, which would inherit it's properties")
                    .WithSummary("Creates a new Application Template.")
            ];
        }

        async Task<CreatedAtRoute<ApplicationTemplate>> TypedHandler(Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            var template = await mediator.Send(command, cancellationToken);
            return TypedResults.CreatedAtRoute(template, nameof(GetApplicationTemplate), new { id = template.Id });
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, ApplicationTemplate>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageApplications], cancellationToken);

        public override async Task<ApplicationTemplate> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var templateEntity = mapper.Map<ApplicationTemplateEntity>(request);

            db.ApplicationTemplates.Add(templateEntity);
            await db.SaveChangesAsync(cancellationToken);

            return mapper.Map<ApplicationTemplate>(templateEntity);
        }
    }
}
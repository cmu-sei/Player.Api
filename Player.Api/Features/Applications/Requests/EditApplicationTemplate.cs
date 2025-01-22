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

namespace Player.Api.Features.Applications;

public class EditApplicationTemplate
{
    [DataContract(Name = "EditApplicationTemplateCommand")]
    public class Command : CreateApplicationTemplate.Command
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPut("application-templates/{id}", TypedHandler)
                    .WithName("updateApplicationTemplate")
                    .WithDescription("Updates an Application Template with the attributes specified")
                    .WithSummary("Updates an Application Template.")
            ];
        }

        async Task<Ok<ApplicationTemplate>> TypedHandler([FromRoute] Guid id, [FromBody] Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.Id = id;
            return TypedResults.Ok(await mediator.Send(command, cancellationToken));
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, ApplicationTemplate>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageApplications], cancellationToken);

        public override async Task<ApplicationTemplate> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var templateToUpdate = await db.ApplicationTemplates
                .SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (templateToUpdate == null)
                throw new EntityNotFoundException<ApplicationTemplate>();

            mapper.Map(request, templateToUpdate);
            await db.SaveChangesAsync(cancellationToken);

            return await db.ApplicationTemplates
                .ProjectTo<ApplicationTemplate>(mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(o => o.Id == templateToUpdate.Id, cancellationToken);
        }
    }
}
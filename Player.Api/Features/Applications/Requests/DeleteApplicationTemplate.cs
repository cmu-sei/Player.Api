// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
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
using Player.Api.ViewModels;

namespace Player.Api.Features.Applications;

public class DeleteApplicationTemplate
{
    [DataContract(Name = "DeleteApplicationTemplateCommand")]
    public class Command : IRequest
    {
        public Guid Id { get; set; }

        /// <summary>
        /// The template that Applications using this one move to before it is deleted. Without it, a
        /// template that any Application still uses is not deleted.
        /// </summary>
        public Guid? ReplacementTemplateId { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapDelete("application-templates/{id}", TypedHandler)
                    .WithName("deleteApplicationTemplate")
                    .WithDescription("Deletes an Application Template with the specified id. If any Application uses the template, the request is refused unless replacementTemplateId names another template, in which case those Applications are moved to it first.")
                    .WithSummary("Deletes an Application Template.")
            ];
        }

        async Task<NoContent> TypedHandler(Guid id, Guid? replacementTemplateId, IMediator mediator, CancellationToken cancellationToken)
        {
            await mediator.Send(new Command { Id = id, ReplacementTemplateId = replacementTemplateId }, cancellationToken);
            return TypedResults.NoContent();
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db) : BaseHandler<Command>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageApplications], cancellationToken);

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var templateToDelete = await db.ApplicationTemplates
               .SingleOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

            if (templateToDelete == null)
                throw new EntityNotFoundException<ApplicationTemplate>();

            var applications = await db.Applications
                .Where(x => x.ApplicationTemplateId == request.Id)
                .ToListAsync(cancellationToken);

            if (applications.Count > 0)
            {
                if (request.ReplacementTemplateId == null)
                {
                    var viewCount = applications.Select(x => x.ViewId).Distinct().Count();
                    throw new ConflictException(
                        $"{(string.IsNullOrWhiteSpace(templateToDelete.Name) ? "This Application Template" : templateToDelete.Name)} is used by {Plural(applications.Count, "Application")} in {Plural(viewCount, "View")}. " +
                        "Choose a template to move them to before deleting it.");
                }

                if (request.ReplacementTemplateId == request.Id)
                    throw new ConflictException("An Application Template cannot be replaced by itself.");

                if (!await db.ApplicationTemplates.AnyAsync(x => x.Id == request.ReplacementTemplateId, cancellationToken))
                    throw new EntityNotFoundException<ApplicationTemplate>("The replacement Application Template was not found.");

                foreach (var application in applications)
                {
                    application.ApplicationTemplateId = request.ReplacementTemplateId;
                }
            }

            db.ApplicationTemplates.Remove(templateToDelete);
            await db.SaveChangesAsync(cancellationToken);
        }

        private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    }
}
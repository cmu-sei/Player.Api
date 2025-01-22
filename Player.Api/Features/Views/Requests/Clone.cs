using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
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

namespace Player.Api.Features.Views;

public class Clone
{
    [DataContract(Name = "CloneViewCommand")]
    public record Command : IRequest<View>
    {
        [JsonIgnore]
        public Guid ViewId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public class Endpoint : IEndpoint
    {
        public RouteHandlerBuilder[] RegisterEndpoints(RouteGroupBuilder group)
        {
            return [
                group.MapPost("views/{id}/clone", TypedHandler)
                    .WithName("cloneView")
                    .WithDescription("Creates a new View from the View specified.")
                    .WithSummary("Clones a View.")
            ];
        }

        async Task<CreatedAtRoute<View>> TypedHandler(Guid id, Command command, IMediator mediator, CancellationToken cancellationToken)
        {
            command.ViewId = id;
            var created = await mediator.Send(command, cancellationToken);
            return TypedResults.CreatedAtRoute(created, "getView", new { id = created.Id });
        }
    }

    public class Handler(IPlayerAuthorizationService authorizationService, PlayerContext db, IMapper mapper) : BaseHandler<Command, View>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.CreateViews], [], [], cancellationToken);

        public override async Task<View> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var view = await db.Views
                .Include(o => o.Teams)
                    .ThenInclude(o => o.Applications)
                .Include(o => o.Teams)
                    .ThenInclude(o => o.Permissions)
                .Include(o => o.Applications)
                    .ThenInclude(o => o.Template)
                .Include(o => o.Files)
                .SingleOrDefaultAsync(o => o.Id == request.ViewId, cancellationToken);

            var newView = view.Clone();
            newView.Name = $"Clone of {newView.Name}";
            newView.Status = ViewStatus.Active;

            newView.Name = string.IsNullOrWhiteSpace(request.Name) ? newView.Name : request.Name;
            newView.Description = string.IsNullOrWhiteSpace(request.Description) ? newView.Description : request.Description;

            //copy view applications
            foreach (var application in view.Applications)
            {
                var newApplication = application.Clone();
                newView.Applications.Add(newApplication);
            }

            //copy teams
            foreach (var team in view.Teams)
            {
                var newTeam = team.Clone();

                //copy team applications
                foreach (var applicationInstance in team.Applications)
                {
                    var newApplicationInstance = applicationInstance.Clone();

                    var application = view.Applications.FirstOrDefault(o => o.Id == applicationInstance.ApplicationId);
                    var newApplication = newView.Applications.FirstOrDefault(o => application != null && o.GetName() == application.GetName());

                    newApplicationInstance.Application = newApplication;

                    newTeam.Applications.Add(newApplicationInstance);
                }

                //copy team permissions
                foreach (var permission in team.Permissions)
                {
                    var newPermission = new TeamPermissionAssignmentEntity(newTeam.Id, permission.PermissionId);
                    newTeam.Permissions.Add(newPermission);
                }

                newView.Teams.Add(newTeam);
            }

            // Copy files - note that the files themselves are not being copied, just the pointers
            foreach (var file in view.Files)
            {
                var cloned = file.Clone();
                cloned.View = newView;
                newView.Files.Add(cloned);
            }

            db.Add(newView);
            await db.SaveChangesAsync(cancellationToken);

            // SaveChanges is called twice because we need the new IDs for each time.
            // Should figure out a better way to do it.
            foreach (var file in newView.Files)
            {
                List<Guid> newTeamIds = new List<Guid>();
                foreach (var team in file.TeamIds)
                {
                    var teamName = view.Teams.FirstOrDefault(t => t.Id == team).Name;
                    var newId = file.View.Teams.FirstOrDefault(t => t.Name == teamName).Id;
                    newTeamIds.Add(newId);
                }
                file.TeamIds = newTeamIds;
            }

            // Update any applications pointing to original files
            foreach (var file in view.Files)
            {
                var newFile = newView.Files.Where(x => x.Path == file.Path).FirstOrDefault();

                if (newFile == null)
                    continue;

                foreach (var application in newView.Applications)
                {
                    if (application.Url != null && application.Url.Contains(file.Id.ToString()))
                    {
                        application.Url = application.Url.Replace(file.Id.ToString(), newFile.Id.ToString());
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return mapper.Map<View>(newView);
        }
    }
}
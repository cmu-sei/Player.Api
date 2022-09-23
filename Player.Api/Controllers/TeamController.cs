// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;
using Player.Api.ViewModels;
using Microsoft.AspNetCore.SignalR;
using Player.Api.Hubs;
using Swashbuckle.AspNetCore.Annotations;

namespace Player.Api.Controllers
{
    public class TeamController : BaseController
    {
        private readonly ITeamService _teamService;
        private readonly IHubContext<TeamHub> _teamHub;
        private readonly INotificationService _notificationService;
        private readonly IAuthorizationService _authorizationService;

        public TeamController(ITeamService teamService, IHubContext<TeamHub> hub, INotificationService notificationService, IAuthorizationService authorizationService)
        {
            _teamService = teamService;
            _teamHub = hub;
            _notificationService = notificationService;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Gets all Teams in the system
        /// </summary>
        /// <remarks>
        /// Returns a list of all of the Teams in the system.
        /// <para />
        /// Only accessible to a SuperUser
        /// </remarks>
        /// <returns></returns>
        [HttpGet("teams")]
        [ProducesResponseType(typeof(IEnumerable<Team>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getTeams")]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var list = await _teamService.GetAsync(ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets all Teams for an View
        /// </summary>
        /// <remarks>
        /// Returns all Teams within a specific View
        /// <para />
        /// Accessible to a SuperUser or a User on an Admin Team within that View
        /// </remarks>
        /// <param name="id">The id of the View</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("views/{id}/teams")]
        [ProducesResponseType(typeof(IEnumerable<Team>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getViewTeams")]
        public async Task<IActionResult> GetByView(Guid id, CancellationToken ct)
        {
            var list = await _teamService.GetByViewIdAsync(id, ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets all Teams for a User by View
        /// </summary>
        /// <remarks>
        /// Returns all Teams within a specific View that a User is a member of
        /// <para />
        /// Accessible to a SuperUser, a User on an Admin Team within that View, or the specified User itself
        /// </remarks>
        /// <param name="userId">The id of the User</param>
        /// <param name="ct"></param>
        /// <param name="viewId">The id of the View</param>
        /// <returns></returns>
        [HttpGet("users/{userId}/views/{viewId}/teams")]
        [ProducesResponseType(typeof(IEnumerable<Team>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getUserViewTeams")]
        public async Task<IActionResult> GetByViewForUser(Guid viewId, Guid userId, CancellationToken ct)
        {
            var list = await _teamService.GetByViewIdForUserAsync(viewId, userId, ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets all Teams for the current User by View
        /// </summary>
        /// <remarks>
        /// Returns all Teams within a specific View that the current User is a member of
        /// <para />
        /// Accessible only to the current User.
        /// <para/>
        /// </remarks>
        /// <param name="id">The id of the View</param>
        /// <param name="ct"></param>
        [HttpGet("me/views/{id}/teams")]
        [SwaggerOperation(OperationId = "getMyViewTeams")]
        public async Task<IActionResult> GetByViewForMe(Guid id, CancellationToken ct)
        {
            var list = await _teamService.GetByViewIdForUserAsync(id, User.GetId(), ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets a specific Team by id
        /// </summary>
        /// <remarks>
        /// Returns the Team with the id specified
        /// <para />
        /// Accessible to a SuperUser, a User on an Admin Team within the Team's View, or a User that is a member of the specified Team
        /// </remarks>
        /// <param name="id">The id of the Team</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("teams/{id}")]
        [ProducesResponseType(typeof(Team), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getTeam")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var team = await _teamService.GetAsync(id, ct);

            if (team == null)
                throw new EntityNotFoundException<Team>();

            return Ok(team);
        }

        /// <summary>
        /// Creates a new Team within an View
        /// </summary>
        /// <remarks>
        /// Creates a new Team within an View with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified View
        /// </remarks>
        /// <param name="id">The id of the View</param>
        /// <param name="form">The data to create the Team with</param>
        /// <param name="ct"></param>
        [HttpPost("views/{id}/teams")]
        [ProducesResponseType(typeof(Team), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "createTeam")]
        public async Task<IActionResult> Create([FromRoute] Guid id, [FromBody] TeamForm form, CancellationToken ct)
        {
            var createdTeam = await _teamService.CreateAsync(id, form, ct);
            return CreatedAtAction(nameof(this.Get), new { id = createdTeam.Id }, createdTeam);
        }

        /// <summary>
        /// Updates a Team
        /// </summary>
        /// <remarks>
        /// Updates a Team with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified View
        /// </remarks>
        /// <param name="id">The id of the Team</param>
        /// <param name="form">The updated Team values</param>
        /// <param name="ct"></param>
        [HttpPut("teams/{id}")]
        [ProducesResponseType(typeof(Team), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "updateTeam")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] TeamForm form, CancellationToken ct)
        {
            var updatedTeam = await _teamService.UpdateAsync(id, form, ct);
            return Ok(updatedTeam);
        }

        /// <summary>
        /// Deletes a Team
        /// </summary>
        /// <remarks>
        /// Deletes a Team with the specified id
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified Team's View
        /// </remarks>
        /// <param name="id">The id of the Team</param>
        /// <param name="ct"></param>
        [HttpDelete("teams/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "deleteTeam")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            await _teamService.DeleteAsync(id, ct);
            return NoContent();
        }

        /// <summary>
        /// Sends a new Team Notification
        /// </summary>
        /// <remarks>
        /// Creates a new Team within an View with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified View
        /// </remarks>
        /// <param name="id">The id of the Team</param>
        /// <param name="incomingData">The data to create the Team with</param>
        /// <param name="ct"></param>
        [HttpPost("teams/{id}/notifications")]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Forbidden)]
        [SwaggerOperation(OperationId = "broadcastToTeam")]
        public async Task<IActionResult> Broadcast([FromRoute] Guid id, [FromBody] Notification incomingData, CancellationToken ct)
        {
            if (!incomingData.IsValid())
            {
                throw new ArgumentException(String.Format("Message was NOT sent to team {0}", id.ToString()));
            }
            var notification = await _notificationService.PostToTeam(id, incomingData, ct);
            if (notification.ToId != id)
            {
                throw new ForbiddenException("Message was not sent to team " + id.ToString());
            }
            await _teamHub.Clients.Group(id.ToString()).SendAsync("Reply", notification);
            return Ok("Message was sent to team " + id.ToString());
        }

        /// <summary>
        /// Sets a User's Primary Team
        /// </summary>
        /// <remarks>
        /// Sets the specified Team as a Primary Team for the specified User
        /// <para />
        /// Accessible only to the specified User
        /// </remarks>
        /// <param name="teamId">The id of the Team</param>
        /// <param name="userId">The id of the User</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpPost("users/{userId}/teams/{teamId}/primary")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "setUserPrimaryTeam")]
        public async Task<IActionResult> SetPrimary(Guid teamId, Guid userId, CancellationToken ct)
        {
            var team = await _teamService.SetPrimaryAsync(teamId, userId, ct);
            return Ok(team);
        }
    }
}

// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Player.Api.Hubs;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;
using Player.Api.ViewModels;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Player.Api.Controllers
{
    public class UserController : BaseController
    {
        private readonly IUserService _userService;
        private readonly IHubContext<UserHub> _userHub;
        private readonly INotificationService _notificationService;
        private readonly IAuthorizationService _authorizationService;

        public UserController(IUserService service, IHubContext<UserHub> userHub, INotificationService notificationService, IAuthorizationService authorizationService)
        {
            _userService = service;
            _notificationService = notificationService;
            _userHub = userHub;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Gets all Users in the system
        /// </summary>
        /// <remarks>
        /// Returns a list of all of the Users in the system.
        /// <para />
        /// Only accessible to a SuperUser
        /// </remarks>
        [HttpGet("users")]
        [ProducesResponseType(typeof(IEnumerable<User>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getUsers")]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var list = await _userService.GetAsync(ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets a specific User by id
        /// </summary>
        /// <remarks>
        /// Returns the User with the id specified
        /// <para />
        /// Accessible to a SuperUser, a User on an Admin Team within any of the specified Users' Views, or a User that shares any Teams with the specified User
        /// </remarks>
        /// <param name="id">The id of the User</param>
        /// <param name="ct"></param>
        [HttpGet("users/{id}")]
        [ProducesResponseType(typeof(User), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getUser")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var user = await _userService.GetAsync(id, ct);

            if (user == null)
                throw new EntityNotFoundException<User>();

            return Ok(user);
        }

        /// <summary>
        /// Gets all Users for an View
        /// </summary>
        /// <remarks>
        /// Returns all Users within a specific View
        /// <para />
        /// Accessible to a SuperUser or a User on an Admin Team within that View
        /// </remarks>
        /// <param name="id">The id of the View</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("views/{id}/users")]
        [ProducesResponseType(typeof(IEnumerable<User>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getViewUsers")]
        public async Task<IActionResult> GetByView(Guid id, CancellationToken ct)
        {
            var list = await _userService.GetByViewAsync(id, ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets all Users for a Team
        /// </summary>
        /// <remarks>
        /// Returns all Users within a specific Team
        /// <para />
        /// Accessible to a SuperUser, a User on an Admin Team within the Team's View, or other members of the Team
        /// </remarks>
        /// <param name="id">The id of the Team</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("teams/{id}/users")]
        [ProducesResponseType(typeof(IEnumerable<User>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getTeamUsers")]
        public async Task<IActionResult> GetByTeam(Guid id, CancellationToken ct)
        {
            var list = await _userService.GetByTeamAsync(id, ct);
            return Ok(list);
        }

        /// <summary>
        /// Adds a User to a Team
        /// </summary>
        /// <remarks>
        /// Adds the specified User to the specified Team
        /// <para />
        /// Accessible to a SuperUser, or a User on an Admin Team within the Team's View
        /// </remarks>
        /// <param name="teamId">The id of the Team</param>
        /// <param name="userId">The id of the User</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpPost("teams/{teamId}/users/{userId}")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "addUserToTeam")]
        public async Task<IActionResult> AddUserToTeam(Guid teamId, Guid userId, CancellationToken ct)
        {
            await _userService.AddToTeamAsync(teamId, userId, ct);
            return Ok();
        }

        /// <summary>
        /// Removes a User from a Team
        /// </summary>
        /// <remarks>
        /// Removes the specified User from the specified Team
        /// <para />
        /// Accessible to a SuperUser, or a User on an Admin Team within the Team's View
        /// </remarks>
        /// <param name="teamId">The id of the Team</param>
        /// <param name="userId">The id of the User</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpDelete("teams/{teamId}/users/{userId}")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "removeUserFromTeam")]
        public async Task<IActionResult> RemoveUserFromTeam(Guid teamId, Guid userId, CancellationToken ct)
        {
            await _userService.RemoveFromTeamAsync(teamId, userId, ct);
            return Ok();
        }

        /// <summary>
        /// Creates a new User
        /// </summary>
        /// <remarks>
        /// Creates a new User with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser
        /// </remarks>
        /// <param name="user">The data to create the User with</param>
        /// <param name="ct"></param>
        [HttpPost("users")]
        [ProducesResponseType(typeof(User), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "createUser")]
        public async Task<IActionResult> Create([FromBody] User user, CancellationToken ct)
        {
            var createdUser = await _userService.CreateAsync(user, ct);
            return CreatedAtAction(nameof(this.Get), new { id = createdUser.Id }, createdUser);
        }

        /// <summary>
        /// Updates a User
        /// </summary>
        /// <remarks>
        /// Updates a User with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser
        /// </remarks>
        /// <param name="id">The Id of the User to update</param>
        /// <param name="user">The updated User values</param>
        /// <param name="ct"></param>
        [HttpPut("users/{id}")]
        [ProducesResponseType(typeof(User), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "updateUser")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] User user, CancellationToken ct)
        {
            var updatedUser = await _userService.UpdateAsync(id, user, ct);
            return Ok(updatedUser);
        }

        /// <summary>
        /// Deletes a User
        /// </summary>
        /// <remarks>
        /// Deletes the User with the specified id
        /// <para />
        /// Accessible only to a SuperUser
        /// </remarks>
        /// <param name="id">The id of the User to delete</param>
        /// <param name="ct"></param>
        [HttpDelete("users/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "deleteUser")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            await _userService.DeleteAsync(id, ct);
            return NoContent();
        }

        /// <summary>
        /// Sends a new User Notification
        /// </summary>
        /// <remarks>
        /// Accessible only to a SuperUser or a User on an Admin User within the specified View
        /// </remarks>
        /// <param name="viewId">The id of the View</param>
        /// <param name="userId">The id of the User</param>
        /// <param name="incomingData">The data to create the Notification</param>
        /// <param name="ct"></param>
        [HttpPost("views/{viewId}/users/{userId}/notifications")]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Forbidden)]
        [SwaggerOperation(OperationId = "broadcastToUser")]
        public async Task<IActionResult> Broadcast([FromRoute] Guid viewId, Guid userId, [FromBody] Notification incomingData, CancellationToken ct)
        {
            if (!incomingData.IsValid())
            {
                throw new ArgumentException(String.Format("Message was NOT sent to user {0} in view {1}", userId.ToString(), viewId.ToString()));
            }
            var notification = await _notificationService.PostToUser(viewId, userId, incomingData, ct);
            if (notification.ToId != userId)
            {
                throw new ForbiddenException(String.Format("Message was NOT sent to user {0} in view {1}", userId.ToString(), viewId.ToString()));
            }

            await _userHub.Clients.Group(String.Format("{0}_{1}", viewId.ToString(), userId.ToString())).SendAsync("Reply", notification);
            return Ok(String.Format("Message was sent to user {0} in view {1}", userId.ToString(), viewId.ToString()));
        }
    }
}

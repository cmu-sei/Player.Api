// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
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
    public class ViewController : BaseController
    {
        private readonly IViewService _viewService;
        private readonly IHubContext<ViewHub> _viewHub;
        private readonly INotificationService _notificationService;
        private readonly IAuthorizationService _authorizationService;

        public ViewController(IViewService viewService, IHubContext<ViewHub> viewHub, INotificationService notificationService, IAuthorizationService authorizationService)
        {
            _viewService = viewService;
            _viewHub = viewHub;
            _notificationService = notificationService;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Gets all View in the system
        /// </summary>
        /// <remarks>
        /// Returns a list of all of the Views in the system.
        /// <para />
        /// Only accessible to a SuperUser
        /// </remarks>
        /// <returns></returns>
        [HttpGet("views")]
        [ProducesResponseType(typeof(IEnumerable<View>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getViews")]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var list = await _viewService.GetAsync(ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets all Views for a User
        /// </summary>
        /// <remarks>
        /// Returns all Views where the specified User is a member of at least one of it's Teams
        /// <para />
        /// Accessible to a SuperUser or the specified User itself
        /// </remarks>
        /// <returns></returns>
        [HttpGet("users/{id}/views")]
        [ProducesResponseType(typeof(IEnumerable<View>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getUserViews")]
        public async Task<IActionResult> GetByUserId(Guid id, CancellationToken ct)
        {
            var list = await _viewService.GetByUserIdAsync(id, ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets all Views for the current User
        /// </summary>
        /// <remarks>
        /// Returns all Views where the current User is a member of at least one of it's Teams
        /// </remarks>
        [HttpGet("me/views")]
        [ProducesResponseType(typeof(IEnumerable<View>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getMyViews")]
        public async Task<IActionResult> GetMy(CancellationToken ct)
        {
            var list = await _viewService.GetByUserIdAsync(User.GetId(), ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets a specific View by id
        /// </summary>
        /// <remarks>
        /// Returns the View with the id specified
        /// <para />
        /// Accessible to a SuperUser or a User that is a member of a Team within the specified View
        /// </remarks>
        /// <param name="id">The id of the View</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("views/{id}")]
        [ProducesResponseType(typeof(View), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getView")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var view = await _viewService.GetAsync(id, ct);

            if (view == null)
                throw new EntityNotFoundException<View>();

            return Ok(view);
        }

        /// <summary>
        /// Creates a new View
        /// </summary>
        /// <remarks>
        /// Creates a new View with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or an Administrator
        /// </remarks>
        /// <param name="view">The data to create the View with</param>
        /// <param name="ct"></param>
        [HttpPost("views")]
        [ProducesResponseType(typeof(View), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "createView")]
        public async Task<IActionResult> Create([FromBody] ViewForm view, CancellationToken ct)
        {
            var createdView = await _viewService.CreateAsync(view, ct);
            return CreatedAtAction(nameof(this.Get), new { id = createdView.Id }, createdView);
        }

        /// <summary>
        /// Updates a View
        /// </summary>
        /// <remarks>
        /// Updates a View with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified View
        /// </remarks>
        /// <param name="id">The Id of the Exericse to update</param>
        /// <param name="view">The updated View values</param>
        /// <param name="ct"></param>
        [HttpPut("views/{id}")]
        [ProducesResponseType(typeof(View), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "updateView")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] View view, CancellationToken ct)
        {
            var updatedView = await _viewService.UpdateAsync(id, view, ct);
            return Ok(updatedView);
        }

        /// <summary>
        /// Deletes a View
        /// </summary>
        /// <remarks>
        /// Deletes a View with the specified id
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified View
        /// </remarks>
        /// <param name="id">The id of the View to delete</param>
        /// <param name="ct"></param>
        [HttpDelete("views/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "deleteView")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            await _viewService.DeleteAsync(id, ct);
            return NoContent();
        }

        /// <summary>
        /// Clones a View
        /// </summary>
        /// <param name="id">Id of the View to be cloned</param>
        /// <param name="viewCloneOverride">OPTIONAL object containing Name and Description to be used for the new clone</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpPost("views/{id}/clone")]
        [ProducesResponseType(typeof(View), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "cloneView")]
        public async Task<IActionResult> Clone(
            Guid id,
            [FromBody, ModelBinder(BinderType = typeof(EmptyBodyModelBinder<ViewCloneOverride>))] ViewCloneOverride viewCloneOverride,
            CancellationToken ct)
        {
            var createdView = await _viewService.CloneAsync(id, viewCloneOverride, ct);
            return CreatedAtAction(nameof(this.Get), new { id = createdView.Id }, createdView);
        }

        /// <summary>
        /// Sends a new View Notification
        /// </summary>
        /// <remarks>
        /// Creates a new View within a View with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin View within the specified View
        /// </remarks>
        /// <param name="id">The id of the View</param>
        /// <param name="incomingData">The data to create the View with</param>
        /// <param name="ct"></param>
        [HttpPost("views/{id}/notifications")]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Forbidden)]
        [SwaggerOperation(OperationId = "broadcastToView")]
        public async Task<IActionResult> Broadcast([FromRoute] Guid id, [FromBody] Notification incomingData, CancellationToken ct)
        {
            if (!incomingData.IsValid())
            {
                throw new ArgumentException(String.Format("Message was NOT sent to view {0}", id.ToString()));
            }
            var notification = await _notificationService.PostToView(id, incomingData, ct);
            if (notification.ToId != id)
            {
                throw new ForbiddenException("Message was not sent to view " + id.ToString());
            }
            await _viewHub.Clients.Group(id.ToString()).SendAsync("Reply", notification);
            return Ok("Message was sent to view " + id.ToString());
        }

        /// <summary>
        /// Gets all Notifications for a View
        /// </summary>
        /// <remarks>
        /// Accessible to a SuperUser
        /// </remarks>
        /// <returns></returns>
        [HttpGet("views/{id}/notifications")]
        [ProducesResponseType(typeof(IEnumerable<View>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getAllViewNotifications")]
        public async Task<IActionResult> GetAllViewNotifications(Guid id, CancellationToken ct)
        {
            var list = await _notificationService.GetAllViewNotificationsAsync(id, ct);
            return Ok(list);
        }

        /// <summary>
        /// Deletes a  Notification
        /// </summary>
        /// <remarks>
        /// Accessible only to a SuperUser or a View Admin
        /// </remarks>
        /// <param name="id">The id of the View</param>
        /// <param name="key">The key of the notification</param>
        /// <param name="ct"></param>
        [HttpDelete("views/{id}/notifications/{key}")]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Forbidden)]
        [SwaggerOperation(OperationId = "deleteNotification")]
        public async Task<IActionResult> DeleteNotification([FromRoute] Guid id, [FromRoute] int key, CancellationToken ct)
        {
            await _notificationService.DeleteAsync(key, ct);
            await _viewHub.Clients.Group(id.ToString()).SendAsync("Delete", key);
            return Ok("Notification deleted - " + key.ToString());
        }

        /// <summary>
        /// Deletes all  Notifications for a view
        /// </summary>
        /// <remarks>
        /// Accessible only to a SuperUser or a View Admin
        /// </remarks>
        /// <param name="id">The id of the View</param>
        /// <param name="ct"></param>
        [HttpDelete("views/{id}/notifications")]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Forbidden)]
        [SwaggerOperation(OperationId = "deleteViewNotifications")]
        public async Task<IActionResult> DeleteViewNotifications([FromRoute] Guid id, CancellationToken ct)
        {
            await _notificationService.DeleteViewNotificationsAsync(id, ct);
            await _viewHub.Clients.Group(id.ToString()).SendAsync("Delete", "all");
            return Ok("Notifications deleted for view " + id.ToString());
        }

    }
}

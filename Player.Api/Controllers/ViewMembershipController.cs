// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Mvc;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;
using Player.Api.ViewModels;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Player.Api.Controllers
{
    public class ViewMembershipController : BaseController
    {
        private readonly IViewMembershipService _viewMembershipService;

        public ViewMembershipController(IViewMembershipService viewMembershipService)
        {
            _viewMembershipService = viewMembershipService;
        }

        /// <summary>
        /// Gets a specific View Membership by id
        /// </summary>
        /// <remarks>
        /// Returns the View Membership with the id specified
        /// <para />
        /// Accessible to Super Users, View Admins for the memberships' View, or the User that the membership belongs to
        /// </remarks>
        /// <param name="id">The id of the View Membership</param>
        /// <returns></returns>
        [HttpGet("view-memberships/{id}")]
        [ProducesResponseType(typeof(ViewMembership), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getViewMembership")]
        public async Task<IActionResult> Get(Guid id)
        {
            var membership = await _viewMembershipService.GetAsync(id);

            if (membership == null)
                throw new EntityNotFoundException<ViewMembership>();

            return Ok(membership);
        }

        /// <summary>
        /// Gets all View Memberships for a User
        /// </summary>
        /// <remarks>
        /// Returns a list of all of the Permissions in the system.
        /// <para />
        /// Accessible to Super Users or the specified User
        /// </remarks>
        /// <returns></returns>
        [HttpGet("users/{userId}/view-memberships")]
        [ProducesResponseType(typeof(IEnumerable<ViewMembership>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getViewMemberships")]
        public async Task<IActionResult> GetByUserId(Guid userId)
        {
            var list = await _viewMembershipService.GetByUserIdAsync(userId);
            return Ok(list);
        }
    }
}

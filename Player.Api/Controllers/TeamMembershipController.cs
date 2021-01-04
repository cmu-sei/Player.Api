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
    public class TeamMembershipController : BaseController
    {
        private readonly ITeamMembershipService _teamMembershipService;

        public TeamMembershipController(ITeamMembershipService teamMembershipService)
        {
            _teamMembershipService = teamMembershipService;
        }

        /// <summary>
        /// Gets a specific Team Membership by id
        /// </summary>
        /// <remarks>
        /// Returns the Team Membership with the id specified
        /// <para />
        /// Accessible to Super Users, View Admins for the membership's View, or the User that the membership belongs to
        /// </remarks>
        /// <param name="id">The id of the Team Membership</param>
        /// <returns></returns>
        [HttpGet("team-memberships/{id}")]
        [ProducesResponseType(typeof(TeamMembership), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getTeamMembership")]
        public async Task<IActionResult> Get(Guid id)
        {
            var membership = await _teamMembershipService.GetAsync(id);

            if (membership == null)
                throw new EntityNotFoundException<TeamMembership>();

            return Ok(membership);
        }

        /// <summary>
        /// Gets all Team Memberships for a User by View
        /// </summary>
        /// <remarks>
        /// Returns a list of all of the Permissions in the system.
        /// <para />
        /// Accessible to Super Users or the specified User
        /// </remarks>
        /// <returns></returns>
        [HttpGet("users/{userId}/views/{viewId}/team-memberships")]
        [ProducesResponseType(typeof(IEnumerable<TeamMembership>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getTeamMemberships")]
        public async Task<IActionResult> GetByViewIdForUser(Guid viewId, Guid userId)
        {
            var list = await _teamMembershipService.GetByViewIdForUserAsync(viewId, userId);
            return Ok(list);
        }

        /// <summary>
        /// Updates a Team Membership
        /// </summary>
        /// <remarks>
        /// Updates a Team Membership with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified View
        /// </remarks>
        /// <param name="id">The id of the Team Membership</param>
        /// <param name="form">The updated Team Membership values</param>
        [HttpPut("team-memberships/{id}")]
        [ProducesResponseType(typeof(TeamMembership), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "updateTeamMembership")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] TeamMembershipForm form)
        {
            var updatedMembership = await _teamMembershipService.UpdateAsync(id, form);
            return Ok(updatedMembership);
        }
    }
}

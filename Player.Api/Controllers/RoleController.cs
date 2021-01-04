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
    public class RoleController : BaseController
    {
        private readonly IRoleService _RoleService;

        public RoleController(IRoleService RoleService)
        {
            _RoleService = RoleService;
        }

        /// <summary>
        /// Gets all Roles in the system
        /// </summary>
        /// <remarks>
        /// Returns a list of all of the Roles in the system.
        /// <para />
        /// Only accessible to a SuperUser
        /// </remarks>
        /// <returns></returns>
        [HttpGet("Roles")]
        [ProducesResponseType(typeof(IEnumerable<Role>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getRoles")]
        public async Task<IActionResult> Get()
        {
            var list = await _RoleService.GetAsync();
            return Ok(list);
        }

        /// <summary>
        /// Gets a specific Role by id
        /// </summary>
        /// <remarks>
        /// Returns the Role with the id specified
        /// <para />
        /// Accessible to all authenticated Users
        /// </remarks>
        /// <param name="id">The id of the Role</param>
        /// <returns></returns>
        [HttpGet("Roles/{id}")]
        [ProducesResponseType(typeof(Role), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getRole")]
        public async Task<IActionResult> Get(Guid id)
        {
            var Role = await _RoleService.GetAsync(id);

            if (Role == null)
                throw new EntityNotFoundException<Role>();

            return Ok(Role);
        }

        /// <summary>
        /// Gets a specific Role by name
        /// </summary>
        /// <remarks>
        /// Returns the Role with the name specified
        /// <para />
        /// Accessible to all authenticated Users
        /// </remarks>
        /// <param name="name">The name of the Role</param>
        /// <returns></returns>
        [HttpGet("Roles/name/{name}")]
        [ProducesResponseType(typeof(Role), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getRoleByName")]
        public async Task<IActionResult> Get(string name)
        {
            var role = await _RoleService.GetAsync(name);
            return Ok(role);
        }

        /// <summary>
        /// Creates a new Role
        /// </summary>
        /// <remarks>
        /// Creates a new Role with the attributes specified
        /// <para />
        /// An Role is a top-level resource that can optionally be the parent of an View specific Application resource, which would inherit it's properties
        /// <para />
        /// Accessible only to a SuperUser
        /// </remarks>
        [HttpPost("Roles")]
        [ProducesResponseType(typeof(Role), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "createRole")]
        public async Task<IActionResult> Create([FromBody] RoleForm form)
        {
            var createdRole = await _RoleService.CreateAsync(form);
            return CreatedAtAction(nameof(this.Get), new { id = createdRole.Id }, createdRole);
        }

        /// <summary>
        /// Updates a Role
        /// </summary>
        /// <remarks>
        /// Updates a Role with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser
        /// </remarks>
        /// <param name="id"></param>
        /// <param name="form">The updated Role values</param>
        /// <returns></returns>
        [HttpPut("Roles/{id}")]
        [ProducesResponseType(typeof(Role), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "updateRole")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] RoleForm form)
        {
            var updatedRole = await _RoleService.UpdateAsync(id, form);
            return Ok(updatedRole);
        }

        /// <summary>
        /// Deletes an Role
        /// </summary>
        /// <remarks>
        /// Deletes a Role with the specified id
        /// <para />
        /// Accessible only to a SuperUser
        /// </remarks>
        /// <param name="id">The id of the Role to delete</param>
        [HttpDelete("Roles/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "deleteRole")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _RoleService.DeleteAsync(id);
            return NoContent();
        }
    }
}

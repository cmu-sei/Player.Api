// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.Services;
using Player.Api.ViewModels;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Swashbuckle.AspNetCore.Annotations;

namespace Player.Api.Controllers
{
    public class ApplicationController : BaseController
    {
        private readonly IApplicationService _applicationService;

        public ApplicationController(IApplicationService service)
        {
            _applicationService = service;
        }

        #region Application-Templates

        /// <summary>
        /// Gets all Application Templates in the system
        /// </summary>
        /// <remarks>
        /// Returns a list of all of the Application Templates in the system.
        /// <para />
        /// Only accessible to a SuperUser
        /// </remarks>
        /// <returns></returns>
        [HttpGet("application-templates")]
        [ProducesResponseType(typeof(IEnumerable<ApplicationTemplate>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getApplicationTemplates")]
        public async Task<IActionResult> GetTemplates(CancellationToken ct)
        {
            var list = await _applicationService.GetTemplatesAsync(ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets a specific Application Template by id
        /// </summary>
        /// <remarks>
        /// Returns the Application Template with the id specified
        /// <para />
        /// Only accessible to a SuperUser
        /// </remarks>
        /// <param name="id">The id of the Application Template</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("application-templates/{id}")]
        [ProducesResponseType(typeof(ApplicationTemplate), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getApplicationTemplate")]
        public async Task<IActionResult> GetTemplate(Guid id, CancellationToken ct)
        {
            var template = await _applicationService.GetTemplateAsync(id, ct);

            if (template == null)
                throw new EntityNotFoundException<ApplicationTemplate>();

            return Ok(template);
        }

        /// <summary>
        /// Creates a new Application Template
        /// </summary>
        /// <remarks>
        /// Creates a new Application Template with the attributes specified
        /// <para />
        /// An Application Template is a top-level resource that can optionally be the parent of an View specific Application resource, which would inherit it's properties
        /// <para />
        /// Accessible only to a SuperUser
        /// </remarks>
        [HttpPost("application-templates")]
        [ProducesResponseType(typeof(ApplicationTemplate), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "createApplicationTemplate")]
        public async Task<IActionResult> Create([FromBody] ApplicationTemplateForm form, CancellationToken ct)
        {
            var createdTemplate = await _applicationService.CreateTemplateAsync(form, ct);
            return CreatedAtAction(nameof(this.GetTemplate), new { id = createdTemplate.Id }, createdTemplate);
        }

        /// <summary>
        /// Updates an Application Template
        /// </summary>
        /// <remarks>
        /// Updates an Application Template with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser
        /// </remarks>
        /// <param name="id"></param>
        /// <param name="form">The updated Application Template values</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpPut("application-templates/{id}")]
        [ProducesResponseType(typeof(ApplicationTemplate), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "updateApplicationTemplate")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] ApplicationTemplateForm form, CancellationToken ct)
        {
            var updatedTemplate = await _applicationService.UpdateTemplateAsync(id, form, ct);
            return Ok(updatedTemplate);
        }

        /// <summary>
        /// Deletes an Application Template
        /// </summary>
        /// <remarks>
        /// Deletes an Application Template with the specified id
        /// <para />
        /// Accessible only to a SuperUser
        /// </remarks>
        /// <param name="id">The id of the Application Template to delete</param>
        /// <param name="ct"></param>
        [HttpDelete("application-templates/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "deleteApplicationTemplate")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            await _applicationService.DeleteTemplateAsync(id, ct);
            return NoContent();
        }

        #endregion

        #region Applications

        /// <summary>
        /// Gets all Applications for an View
        /// </summary>
        /// <remarks>
        /// Returns all Applications assigned to a specific View
        /// <para />
        /// Accessible to a SuperUser or a User on an Admin Team within that View
        /// </remarks>
        /// <param name="id">The id of the View</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("views/{id}/applications")]
        [ProducesResponseType(typeof(IEnumerable<Application>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getViewApplications")]
        public async Task<IActionResult> GetApplicationsByView(Guid id, CancellationToken ct)
        {
            var list = await _applicationService.GetApplicationsByViewAsync(id, ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets a specific Application by id
        /// </summary>
        /// <remarks>
        /// Returns the Application with the id specified
        /// <para />
        /// Accessible to a SuperUser or a User on an Admin Team in the Application's assigned View
        /// </remarks>
        /// <param name="id">The id of the Application</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("applications/{id}")]
        [ProducesResponseType(typeof(Application), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getApplication")]
        public async Task<IActionResult> GetApplication(Guid id, CancellationToken ct)
        {
            var application = await _applicationService.GetApplicationAsync(id, ct);

            if (application == null)
                throw new EntityNotFoundException<Application>();

            return Ok(application);
        }

        /// <summary>
        /// Creates a new Application within an View
        /// </summary>
        /// <remarks>
        /// Creates a new Application within an View with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified View
        /// </remarks>
        /// <param name="id">The id of the View</param>
        /// <param name="application">The data to create the Application with</param>
        /// <param name="ct"></param>
        [HttpPost("views/{id}/applications")]
        [ProducesResponseType(typeof(Application), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "createApplication")]
        public async Task<IActionResult> CreateApplication([FromRoute] Guid id, [FromBody] Application application, CancellationToken ct)
        {
            var createdApplication = await _applicationService.CreateApplicationAsync(id, application, ct);
            return CreatedAtAction(nameof(this.GetApplication), new { id = createdApplication.Id }, createdApplication);
        }

        /// <summary>
        /// Updates an Application
        /// </summary>
        /// <remarks>
        /// Updates an Application with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team in the Application's assigned View
        /// </remarks>
        [HttpPut("applications/{id}")]
        [ProducesResponseType(typeof(Application), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "updateApplication")]
        public async Task<IActionResult> UpdateApplication([FromRoute] Guid id, [FromBody] Application application, CancellationToken ct)
        {
            var updatedApplication = await _applicationService.UpdateApplicationAsync(id, application, ct);
            return Ok(updatedApplication);
        }

        /// <summary>
        /// Deletes an Application
        /// </summary>
        /// <remarks>
        /// Deletes an Application with the specified id
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified Team's View
        /// </remarks>
        /// <param name="id">The id of the Application</param>
        /// <param name="ct"></param>
        [HttpDelete("applications/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "deleteApplication")]
        public async Task<IActionResult> DeleteApplication(Guid id, CancellationToken ct)
        {
            await _applicationService.DeleteApplicationAsync(id, ct);
            return NoContent();
        }

        #endregion

        #region Application-Instances

        /// <summary>
        /// Gets all Applications Instances for a Team
        /// </summary>
        /// <remarks>
        /// Returns all Application Instances assigned to a specific Team
        /// <para />
        /// Accessible to a SuperUser, a User on an Admin Team in the Team's View, or any User on the specified Team
        /// </remarks>
        /// <param name="id">The id of the Team</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("teams/{id}/application-instances")]
        [ProducesResponseType(typeof(IEnumerable<ApplicationInstance>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getTeamApplicationInstances")]
        public async Task<IActionResult> GetInstancesByTeam(Guid id, CancellationToken ct)
        {
            var list = await _applicationService.GetInstancesByTeamAsync(id, ct);
            return Ok(list);
        }

        /// <summary>
        /// Gets a specific Application Instance by id
        /// </summary>
        /// <remarks>
        /// Returns the Application Instance with the id specified
        /// <para />
        /// Accessible to a SuperUser or a User on an Admin Team in the Application Instance's Team's View
        /// </remarks>
        /// <param name="id">The id of the Application Instance</param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [HttpGet("application-instances/{id}")]
        [ProducesResponseType(typeof(ApplicationInstance), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getApplicationInstance")]
        public async Task<IActionResult> GetInstance(Guid id, CancellationToken ct)
        {
            var instance = await _applicationService.GetInstanceAsync(id, ct);

            if (instance == null)
                throw new EntityNotFoundException<ApplicationInstance>();

            return Ok(instance);
        }

        /// <summary>
        /// Creates a new Application Instance within a Team
        /// </summary>
        /// <remarks>
        /// Creates a new Application Instance within a Team with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team within the specified Team's View
        /// </remarks>
        /// <param name="id">The id of the Team</param>
        /// <param name="instance">The data to create the Application Instance with</param>
        /// <param name="ct"></param>
        [HttpPost("teams/{id}/application-instances")]
        [ProducesResponseType(typeof(ApplicationInstance), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "createApplicationInstance")]
        public async Task<IActionResult> CreateApplicationInstance([FromRoute] Guid id, [FromBody] ApplicationInstanceForm instance, CancellationToken ct)
        {
            var createdInstance = await _applicationService.CreateInstanceAsync(id, instance, ct);
            return CreatedAtAction(nameof(this.GetInstance), new { id = createdInstance.Id }, createdInstance);
        }

        /// <summary>
        /// Updates an Application Instance
        /// </summary>
        /// <remarks>
        /// Updates an Application Instance with the attributes specified
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team in the Application Instances's Team's View
        /// </remarks>
        /// <param name="id">The id of the Application Instance</param>
        /// <param name="instance">The updated instance values</param>
        /// <param name="ct"></param>
        [HttpPut("application-instances/{id}")]
        [ProducesResponseType(typeof(ApplicationInstance), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "updateApplicationInstance")]
        public async Task<IActionResult> UpdateApplicationInstance([FromRoute] Guid id, [FromBody] ApplicationInstanceForm instance, CancellationToken ct)
        {
            var updatedInstance = await _applicationService.UpdateInstanceAsync(id, instance, ct);
            return Ok(updatedInstance);
        }

        /// <summary>
        /// Deletes an Application Instance
        /// </summary>
        /// <remarks>
        /// Deletes an Application Instance with the specified id
        /// <para />
        /// Accessible only to a SuperUser or a User on an Admin Team in the Application Instances's Team's View
        /// </remarks>
        /// <param name="id">The id of the Application</param>
        /// <param name="ct"></param>
        [HttpDelete("application-instances/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "deleteApplicationInstance")]
        public async Task<IActionResult> DeleteApplicationInstance(Guid id, CancellationToken ct)
        {
            await _applicationService.DeleteInstanceAsync(id, ct);
            return NoContent();
        }

        #endregion
    }
}

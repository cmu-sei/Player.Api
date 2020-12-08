/*
Crucible
Copyright 2020 Carnegie Mellon University.
NO WARRANTY. THIS CARNEGIE MELLON UNIVERSITY AND SOFTWARE ENGINEERING INSTITUTE MATERIAL IS FURNISHED ON AN "AS-IS" BASIS. CARNEGIE MELLON UNIVERSITY MAKES NO WARRANTIES OF ANY KIND, EITHER EXPRESSED OR IMPLIED, AS TO ANY MATTER INCLUDING, BUT NOT LIMITED TO, WARRANTY OF FITNESS FOR PURPOSE OR MERCHANTABILITY, EXCLUSIVITY, OR RESULTS OBTAINED FROM USE OF THE MATERIAL. CARNEGIE MELLON UNIVERSITY DOES NOT MAKE ANY WARRANTY OF ANY KIND WITH RESPECT TO FREEDOM FROM PATENT, TRADEMARK, OR COPYRIGHT INFRINGEMENT.
Released under a MIT (SEI)-style license, please see license.txt or contact permission@sei.cmu.edu for full terms.
[DISTRIBUTION STATEMENT A] This material has been approved for public release and unlimited distribution.  Please see Copyright notice for non-US Government use and distribution.
Carnegie Mellon(R) and CERT(R) are registered in the U.S. Patent and Trademark Office by Carnegie Mellon University.
DM20-0181
*/

using System;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Player.Api.Services;
using Player.Api.ViewModels;
using Swashbuckle.AspNetCore.Annotations;

namespace Player.Api.Controllers
{
    public class FileController : BaseController
    {
        private readonly IFileService _fileService;

        public FileController(IFileService fileService)
        {
            _fileService = fileService;
        }

        /// <summary> Upload a file </summary>
        /// <param name="form"> The settings for the file </param> 
        /// <param name="ct"></param>
        [HttpPost("files")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "uploadFile")]
        public async Task<IActionResult> Upload([FromForm] FileForm form, CancellationToken ct)
        {
            var result = await _fileService.UploadAsync(form, ct);
            return CreatedAtAction(nameof(this.Get), new { id = result.id }, result);
        }

        /// <summary> Get all files in the system </summary>
        /// <param name="ct"></param>
        [HttpGet("files")]
        [ProducesResponseType(typeof(IEnumerable<FileModel>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getAllFiles")]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var files = await _fileService.GetAsync(ct);
            return Ok(files);
        }

        /// <summary> Get all files in a view </summary>
        /// <param name="viewId">The id of the view</param>
        /// <param name="ct"></param>
        [HttpGet("views/{viewId}/files")]
        [ProducesResponseType(typeof(FileModel), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getViewFiles")]
        public async Task<IActionResult> GetViewFiles(Guid viewId, CancellationToken ct)
        {
            var files = await _fileService.GetByViewAsync(viewId, ct);
            return Ok(files);
        }

        /// <summary> Get all files in a team </summary>
        /// <param name="teamId"> The id of the team </param>
        /// <param name="ct"></param>
        [HttpGet("teams/{teamId}/files")]
        [ProducesResponseType(typeof(FileModel), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getTeamFiles")]
        public async Task<IActionResult> GetTeamFiles(Guid teamId, CancellationToken ct)
        {
            var file = await _fileService.GetByTeamAsync(teamId, ct);
            return Ok(file);
        }

        /// <summary> Get a specific file by id </summary>
        /// <param name="fileId"> The id of the file </param>
        /// <param name="ct"></param>
        [HttpGet("/files/{fileId}")]
        [ProducesResponseType(typeof(FileModel), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getById")]
        public async Task<IActionResult> GetById(Guid fileId, CancellationToken ct)
        {
            var file = await _fileService.GetByIdAsync(fileId, ct);
            return Ok(file);
        }

        /// <summary> Update a file </summary>
        /// <remarks> Takes a form with fields for team IDs and a new file. File can be assigned to different teams and/or replaced.
        /// The file entry will be changed to point at the newly uploaded file. </remarks>
        /// <param name="fileId"> The id of the file </param>
        /// <param name="form"> The settings for the file </param> 
        /// <param name="ct"></param>
        [HttpPut("/files/{fileId}")]
        [ProducesResponseType(typeof(FileModel), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "updateFile")]
        public async Task<IActionResult> Update(Guid fileId, [FromForm] FileUpdateForm form, CancellationToken ct)
        {
            var updated = await _fileService.UpdateAsync(fileId, form, ct);
            return Ok(updated);
        }

        /// <summary> Delete a file </summary>
        /// <param name="fileId"> The id of the file </param>
        /// <param name="ct"></param>
        [HttpDelete("/files/{fileId}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "deleteFile")]
        public async Task<IActionResult> Delete(Guid fileId, CancellationToken ct)
        {
            await _fileService.DeleteAsync(fileId, ct);
            return NoContent();
        }
    }
}
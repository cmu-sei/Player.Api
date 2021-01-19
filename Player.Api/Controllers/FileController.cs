// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
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

        /// <summary> Upload file(s) </summary>
        /// <remarks> File objects will be returned in the same order as their respective files within the form. </remarks>
        /// <param name="form"> The files to upload and their settings </param>
        /// <param name="ct"></param>
        [HttpPost("files")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "uploadMultipleFiles")]
        public async Task<IActionResult> UploadMultiple([FromForm] FileForm form, CancellationToken ct)
        {
            var result = await _fileService.UploadAsync(form, ct);
            return CreatedAtAction(nameof(this.Get), result);
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

        /// <summary> Get all files in a view accessable to the calling user </summary>
        /// <param name="viewId">The id of the view</param>
        /// <param name="ct"></param>
        [HttpGet("views/{viewId}/files")]
        [ProducesResponseType(typeof(IEnumerable<FileModel>), (int)HttpStatusCode.OK)]
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
        [ProducesResponseType(typeof(IEnumerable<FileModel>), (int)HttpStatusCode.OK)]
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

        /// <summary> Download a file by id </summary>
        /// <remarks> This endpoint downloads the actual file, files/{fileId} returns the DB entry for a file </remarks>
        /// <param name="fileId"> The id of the file </param>
        /// <param name="ct"></param>
        [HttpGet("/files/download/{fileId}")]
        [ProducesResponseType(typeof(FileResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "download")]
        public async Task<IActionResult> Download(Guid fileId, CancellationToken ct)
        {
            (var stream, var fileName) = await _fileService.DownloadAsync(fileId, ct);

            if (IsPdf(fileName))
            {
                Response.Headers.Add("Content-Disposition", "inline");
                return File(stream, "application/pdf", fileName);
            }
                
            else if (IsImage(fileName))
            {
                Response.Headers.Add("Content-Disposition", "inline");
                var ext = Path.GetExtension(fileName);
                return File(stream, "image/" + ext, fileName);
            }

            // If this is wrapped in an Ok, it throws an exception            
            return File(stream, "application/octet-stream", fileName);
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

        private bool IsPdf(string file)
        {
            return file.EndsWith(".pdf");
        }

        // Will need to update this method if we want to support more image types.
        private bool IsImage(string file)
        {
            return file.EndsWith(".jpg") || file.EndsWith(".jpeg") || file.EndsWith(".png") || file.EndsWith(".bmp") || 
                file.EndsWith(".heic") || file.EndsWith(".gif"); 
        }
    }
}
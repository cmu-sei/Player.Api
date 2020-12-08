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

        /// <summary> Upload a file to a view </summary>
        /// <param name="viewId">The id of the view</param>
        /// <param name="ct"></param>
        /// <param name="file"></param>
        [HttpPost("views/{viewId}/files")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "uploadFile")]
        public async Task<IActionResult> Upload(Guid viewId, CancellationToken ct, IFormFile file)
        {
            var result = await _fileService.UploadAsync(file, viewId, ct);
            return Ok(result);
        }

        /// <summary> Get all files in the system </summary>
        /// <param name="ct"></param>
        [HttpGet("views/files")]
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

        
    }
}
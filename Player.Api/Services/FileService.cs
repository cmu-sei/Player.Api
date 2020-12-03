/*
Crucible
Copyright 2020 Carnegie Mellon University.
NO WARRANTY. THIS CARNEGIE MELLON UNIVERSITY AND SOFTWARE ENGINEERING INSTITUTE MATERIAL IS FURNISHED ON AN "AS-IS" BASIS. CARNEGIE MELLON UNIVERSITY MAKES NO WARRANTIES OF ANY KIND, EITHER EXPRESSED OR IMPLIED, AS TO ANY MATTER INCLUDING, BUT NOT LIMITED TO, WARRANTY OF FITNESS FOR PURPOSE OR MERCHANTABILITY, EXCLUSIVITY, OR RESULTS OBTAINED FROM USE OF THE MATERIAL. CARNEGIE MELLON UNIVERSITY DOES NOT MAKE ANY WARRANTY OF ANY KIND WITH RESPECT TO FREEDOM FROM PATENT, TRADEMARK, OR COPYRIGHT INFRINGEMENT.
Released under a MIT (SEI)-style license, please see license.txt or contact permission@sei.cmu.edu for full terms.
[DISTRIBUTION STATEMENT A] This material has been approved for public release and unlimited distribution.  Please see Copyright notice for non-US Government use and distribution.
Carnegie Mellon(R) and CERT(R) are registered in the U.S. Patent and Trademark Office by Carnegie Mellon University.
DM20-0181
*/

using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Z.EntityFramework.Plus;

namespace Player.Api.Services
{
    public interface IFileService
    {
        Task<File> UploadAsync(IFormFile file, Guid viewId, CancellationToken ct);
    }

    public class FileService : IFileService
    {
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;

        public FileService(IPrincipal user, IAuthorizationService authService)
        {
            _user = user as ClaimsPrincipal;
            _authorizationService = authService;
        }

        public async Task<File> UploadAsync(IFormFile file, Guid viewId, CancellationToken ct)
        {
            // Sanitize file name
            // Validate file size
            // Ensure user has manage permissions on this view
            // Set filepath = basepath + viewId + sanitized name
            // create directory for this view (Directory.CreateDirectory)
            // Write file to disk
            // Save file model to DB
            // Return the model that was saved to DB
            return null;
        }
    }
}
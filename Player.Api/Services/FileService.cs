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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.Exceptions;
using Player.Api.ViewModels;
using Player.Api.Options;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace Player.Api.Services
{
    public interface IFileService
    {
        Task<FileModel> UploadAsync(IFormFile file, Guid viewId, CancellationToken ct);
        Task<IEnumerable<FileModel>> GetAsync(CancellationToken ct);
        Task<IEnumerable<FileModel>> GetByViewAsync(Guid viewId, CancellationToken ct);
        Task<FileModel> GetByIdAsync(Guid fileId, CancellationToken ct);
        Task<bool> DeleteAsync (Guid fileId, CancellationToken ct);
    }

    public class FileService : IFileService
    {
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        private readonly FileUploadOptions _fileUploadOptions;
        private readonly PlayerContext _context;
        private readonly IMapper _mapper;

        public FileService(IPrincipal user, IAuthorizationService authService, FileUploadOptions fileOptions, PlayerContext context, IMapper mapper)
        {
            _user = user as ClaimsPrincipal;
            _authorizationService = authService;
            _fileUploadOptions = fileOptions;
            _context = context;
            _mapper = mapper;
        }

        public async Task<FileModel> UploadAsync(IFormFile file, Guid viewId, CancellationToken ct)
        {
            // TODO: Make sure file type is acceptable
            var name = SanitizeFileName(file.FileName);
            var size = file.Length;

            if (size > _fileUploadOptions.maxSize)
            {
                throw new InvalidOperationException($"File size exceeds the {_fileUploadOptions.maxSize} limit");
            }

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ManageViewRequirement(viewId))).Succeeded)
                throw new ForbiddenException();
            
            var folderPath = Path.Combine(_fileUploadOptions.basePath, viewId.ToString());
            var filepath = Path.Combine(folderPath, name);

            Directory.CreateDirectory(folderPath);

            using (var stream = File.Create(filepath))
            {
                await file.CopyToAsync(stream);
            }

            var form = new FileForm(name, viewId, filepath);
            var entity = _mapper.Map<FileEntity>(form);

            _context.Files.Add(entity);
            await _context.SaveChangesAsync(ct);

            return _mapper.Map<FileModel>(entity);
        }

        public async Task<IEnumerable<FileModel>> GetAsync(CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();
            
            var files = await _context.Files.ToListAsync();

            return _mapper.Map<IEnumerable<FileModel>>(files);
        }

        public async Task<IEnumerable<FileModel>> GetByViewAsync(Guid viewId, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ManageViewRequirement(viewId))).Succeeded)
                throw new ForbiddenException();
            
            var files = await _context.Files
                .Where(f => f.ViewId == viewId)
                .ToListAsync();
            
            return _mapper.Map<IEnumerable<FileModel>>(files);
        }

        public async Task<FileModel> GetByIdAsync(Guid fileId, CancellationToken ct)
        {            
            var file = await _context.Files
                .Where(f => f.Id == fileId)
                .SingleOrDefaultAsync(ct);
            
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewMemberRequirement(file.ViewId))).Succeeded)
                throw new ForbiddenException();
            
            return _mapper.Map<FileModel>(file);
        }

        public async Task<bool> DeleteAsync(Guid fileId, CancellationToken ct)
        {
            var toDelete = await _context.Files
                .Where(f => f.Id == fileId)
                .SingleOrDefaultAsync(ct);
            
            if (toDelete == null)
                throw new EntityNotFoundException<FileModel>();
            
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(toDelete.ViewId))).Succeeded)
                throw new ForbiddenException();
            
            // If this is the last pointer to the file, the file should be deleted. Else, just delete the pointer
            var pointerCount = await _context.Files
                .Where(f => f.Path == toDelete.Path)
                .CountAsync(ct);
            
            // Must delete file on disk as well
            if (pointerCount <= 1)
            {
                File.Delete(toDelete.Path);
            }
            _context.Files.Remove(toDelete);
            await _context.SaveChangesAsync(ct);
            return true;
        }

        private string SanitizeFileName(string name)
        {
            var ret = "";
            var disallowed = Path.GetInvalidFileNameChars();
            foreach (var c in name)
                if (!disallowed.Contains(c))
                    ret += c;
            return ret;
        }
    }
}
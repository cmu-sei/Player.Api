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
using Player.Api.Extensions;
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
        Task<IEnumerable<FileModel>> UploadAsync(FileForm form, CancellationToken ct);
        Task<IEnumerable<FileModel>> GetAsync(CancellationToken ct);
        Task<IEnumerable<FileModel>> GetByViewAsync(Guid viewId, CancellationToken ct);
        Task<IEnumerable<FileModel>> GetByTeamAsync(Guid teamId, CancellationToken ct);
        Task<FileModel> GetByIdAsync(Guid fileId, CancellationToken ct);
        Task<FileModel> UpdateAsync(Guid fileId, FileUpdateForm form, CancellationToken ct);
        Task<bool> DeleteAsync (Guid fileId, CancellationToken ct);
    }

    public class FileService : IFileService
    {
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        private readonly FileUploadOptions _fileUploadOptions;
        private readonly PlayerContext _context;
        private readonly IMapper _mapper;
        private readonly ITeamService _teamService;

        public FileService(IPrincipal user, IAuthorizationService authService, FileUploadOptions fileOptions, PlayerContext context, IMapper mapper, ITeamService teamService)
        {
            _user = user as ClaimsPrincipal;
            _authorizationService = authService;
            _fileUploadOptions = fileOptions;
            _context = context;
            _mapper = mapper;
            _teamService = teamService;
        }

        public async Task<IEnumerable<FileModel>> UploadAsync(FileForm form, CancellationToken ct)
        {
            if (form.teamIds == null)
                throw new ForbiddenException("File must be assigned to at least one team");
            
            List<FileModel> models = new List<FileModel>();
            foreach(var fp in form.ToUpload)
            {
                if (!ValidateFileExtension(fp.FileName))
                    throw new ForbiddenException("Invalid file extension");

                // Sanitization is probably unnecessary since this name is only stored in db 
                var name = SanitizeFileName(fp.FileName);

                var filePath = await uploadFile(fp, form.viewId, GetNameToStore(name));

                var entity = _mapper.Map<FileEntity>(form);
                entity.Name = name;
                entity.Path = filePath;
                // ID is set here so we can return a list of the uploaded files without calling save changes more than once
                entity.Id = Guid.NewGuid();
                _context.Files.Add(entity);

                models.Add(_mapper.Map<FileModel>(entity));
            }

            await _context.SaveChangesAsync(ct);
            return models;

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
            var userId = _user.GetId();
            var teams = await _teamService.GetByViewIdForUserAsync(viewId, userId, ct);
            teams = teams.Where(t => t.IsMember);
            var accessable = new List<FileModel>();
            foreach (var team in teams)
            {
                var files = _context.Files
                    .AsEnumerable()
                    .Where(f => f.TeamIds.Contains(team.Id))
                    .ToList();

                accessable.AddRange(_mapper.Map<IEnumerable<FileModel>>(files));
            }
            return _mapper.Map<IEnumerable<FileModel>>(accessable);
        }

        public async Task<IEnumerable<FileModel>> GetByTeamAsync(Guid teamId, CancellationToken ct)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new TeamMemberRequirement(teamId))).Succeeded
                && !(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();
            
            var files = _context.Files
                .AsEnumerable()
                .Where(f => f.TeamIds.Contains(teamId))
                .ToList();
            
            return _mapper.Map<IEnumerable<FileModel>>(files);
        }

        public async Task<FileModel> GetByIdAsync(Guid fileId, CancellationToken ct)
        {            
            var file = await _context.Files
                .Where(f => f.Id == fileId)
                .SingleOrDefaultAsync(ct);
            
            // The user can see this file if they are in at least one of the teams it is assigned to
            var canAccess = false;
            foreach (var teamId in file.TeamIds)
            {
                if ((await _authorizationService.AuthorizeAsync(_user, null, new TeamMemberRequirement(teamId))).Succeeded)
                {
                    canAccess = true;
                    break;
                }
            }
            // If user is not on any teams, they can't access the file unless they are an admin    
            if (!canAccess && !(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                throw new ForbiddenException();
            
            return _mapper.Map<FileModel>(file);
        }

        public async Task<FileModel> UpdateAsync(Guid fileId, FileUpdateForm form, CancellationToken ct)
        {
            var entity = await _context.Files
                .Where(f => f.Id == fileId)
                .SingleOrDefaultAsync(ct);
            
            if (entity == null)
                throw new EntityNotFoundException<FileModel>();
            
            // This authorization check assumes all teams for the file are in the same view
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ManageViewRequirement(entity.ViewId))).Succeeded)
                throw new ForbiddenException();
            
            // File pointed to is being changed
            if (form.ToUpload != null)
            {
                if (!ValidateFileExtension(form.ToUpload.FileName))
                    throw new ForbiddenException("Invalid file extension");

                var name = SanitizeFileName(form.ToUpload.FileName);

                var filePath = await uploadFile(form.ToUpload, entity.ViewId, GetNameToStore(name));

                // File is now on disk, check if old file should be deleted (only has the one pointer)
                if (await lastPointer(entity.Path, ct))
                    File.Delete(entity.Path);
                
                // Move pointer to new file
                entity.Path = filePath;
                entity.Name = name;
            }
            // Teams are being changed
            if (form.TeamIds != null)
                entity.TeamIds = form.TeamIds;

            _context.Update(entity);
            await _context.SaveChangesAsync(ct);
            return _mapper.Map<FileModel>(entity);
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
            if (await lastPointer(toDelete.Path, ct))
                File.Delete(toDelete.Path);

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

        // Ensure that the file has a valid extension
        private bool ValidateFileExtension(string name)
        {
            var valid = false;
            foreach (var ext in _fileUploadOptions.allowedExtensions)
            {
                if (name.EndsWith(ext))
                {
                    valid = true;
                    break;
                }
            }

            return valid;
        }

        private async Task<bool> lastPointer(string path, CancellationToken ct)
        {
            var pointerCount = await _context.Files
                .Where(f => f.Path == path)
                .CountAsync(ct);
            
            return pointerCount == 1;
        }

        private async Task<string> uploadFile(IFormFile file, Guid viewId, string name)
        {
            var size = file.Length;

            if (size > _fileUploadOptions.maxSize)
                throw new InvalidOperationException($"File size exceeds the {_fileUploadOptions.maxSize} limit");

            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ManageViewRequirement(viewId))).Succeeded)
                throw new ForbiddenException();
            
            var folderPath = Path.Combine(_fileUploadOptions.basePath, viewId.ToString());
            var filepath = Path.Combine(folderPath, name);

            Directory.CreateDirectory(folderPath);

            using (var stream = File.Create(filepath))
                await file.CopyToAsync(stream);

            return filepath;
        }

        private string GetNameToStore(string originalName)
        {
            var toStore = Guid.NewGuid().ToString();
            var ext = originalName.Split('.')[1];
            toStore += '.' + ext;
            return toStore;
        }
    }
}
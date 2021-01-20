// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

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
using System.Text;
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
        Task<Tuple<FileStream, string>> DownloadAsync(Guid fileId, CancellationToken ct);
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
            
            var viewEntity = await _context.Views
                .Where(v => v.Id == form.viewId)
                .SingleOrDefaultAsync(ct);
            
            // Ensure all teams are in the same view
            if (!await TeamsInSameView(viewEntity.Id, form.teamIds, ct))
                throw new ForbiddenException("Teams must be in same view.");

            // Ensyre user can manage this view
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(viewEntity.Id))).Succeeded)
                throw new ForbiddenException("Insuffcient Permissions");
            
            List<FileModel> models = new List<FileModel>();
            foreach(var fp in form.ToUpload)
            {
                if (!ValidateFileExtension(fp.FileName))
                    throw new ForbiddenException("Invalid file extension");

                var name = SanitizeFileName(fp.FileName);

                var filePath = await uploadFile(fp, form.viewId, GetNameToStore(name));

                var entity = _mapper.Map<FileEntity>(form);
                entity.Name = name;
                entity.Path = filePath;
                // ID is set here so we can return a list of the uploaded files without calling save changes more than once
                entity.Id = Guid.NewGuid();
                _context.Files.Add(entity);

                models.Add(_mapper.Map<FileModel>(entity));

                // Add file to list in view 
                viewEntity.Files.Add(entity);
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
            var accessable = new List<FileModel>();
            
            foreach (var team in teams)
            {
                accessable.AddRange(_mapper.Map<IEnumerable<FileModel>>(await GetByTeamAsync(team.Id, ct)));
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
                .Include(f => f.View)
                .SingleOrDefaultAsync(ct);
            
            if (file == null)
                throw new EntityNotFoundException<FileModel>(); 
            
            EnsureAccessFile(file);
            
            return _mapper.Map<FileModel>(file);
        }

        public async Task<Tuple<FileStream, string>> DownloadAsync(Guid fileId, CancellationToken ct)
        {
            var file = await _context.Files
                .Where(f => f.Id == fileId)
                .Include(f => f.View)
                .SingleOrDefaultAsync(ct);

            if (file == null)
                throw new EntityNotFoundException<FileModel>();
            
            EnsureAccessFile(file);

            return Tuple.Create(File.OpenRead(file.Path), file.Name);            
        }

        public async Task<FileModel> UpdateAsync(Guid fileId, FileUpdateForm form, CancellationToken ct)
        {
            var entity = await _context.Files
                .Where(f => f.Id == fileId)
                .Include(f => f.View)
                .SingleOrDefaultAsync(ct);
            
            if (entity == null)
                throw new EntityNotFoundException<FileModel>();
            
            if (! await TeamsInSameView(entity.View.Id, form.TeamIds, ct))
                throw new ForbiddenException("Teams must be in same view");
            
            // This authorization check assumes all teams for the file are in the same view, but we have verified
            // that that is the case with the above check.
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ManageViewRequirement(entity.View.Id))).Succeeded)
                throw new ForbiddenException();
                
            
            // File pointed to is being changed
            if (form.ToUpload != null)
            {
                if (!ValidateFileExtension(form.ToUpload.FileName))
                    throw new ForbiddenException("Invalid file extension");

                var name = SanitizeFileName(form.ToUpload.FileName);

                var filePath = await uploadFile(form.ToUpload, entity.View.Id, GetNameToStore(name));

                // File is now on disk, check if old file should be deleted (only has the one pointer)
                if (await lastPointer(entity.Path, ct))
                    File.Delete(entity.Path);
                
                // Move pointer to new file
                entity.Path = filePath;
                entity.Name = name;
            }
            // Teams are being changed and/or file is being renamed
            else
            {
                entity.TeamIds = form.TeamIds;
                entity.Name = form.Name;
            }

            _context.Update(entity);
            await _context.SaveChangesAsync(ct);
            return _mapper.Map<FileModel>(entity);
        }

        public async Task<bool> DeleteAsync(Guid fileId, CancellationToken ct)
        {
            var toDelete = await _context.Files
                .Where(f => f.Id == fileId)
                .Include(f => f.View)
                .SingleOrDefaultAsync(ct);
            
            if (toDelete == null)
                throw new EntityNotFoundException<FileModel>();
            
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(toDelete.View.Id))).Succeeded)
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
            var ret = new StringBuilder("");
            var disallowed = Path.GetInvalidFileNameChars();
            foreach (var c in name)
                if (!disallowed.Contains(c))
                    ret.Append(c);
            return ret.ToString();
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
            var ext = Path.GetExtension(originalName);
            toStore += '.' + ext;
            return toStore;
        }

        private async void EnsureAccessFile(FileEntity file)
        {
            // The user can see this file if they are in at least one of the teams it is assigned to
            var canAccess = (await _authorizationService.AuthorizeAsync(_user, null, new TeamsMemberRequirement(file.TeamIds))).Succeeded;
            // If user is not on any teams, they can't access the file unless they are a view admin    
            if (!canAccess && !(await _authorizationService.AuthorizeAsync(_user, null, new ViewAdminRequirement(file.View.Id))).Succeeded)
                throw new ForbiddenException();
        }

        private async Task<bool> TeamsInSameView(Guid viewId, List<Guid> teamIds, CancellationToken ct)
        {
            var viewEntity = await _context.Views
                .Where(v => v.Id == viewId)
                .Include(v => v.Teams)
                .SingleOrDefaultAsync(ct);
            
            // Ensure all teams are in the same view
            foreach (var teamId in teamIds)
            {
                if (!viewEntity.Teams.Any(t => t.Id == teamId))
                    return false;
            }

            return true;
        }
    }
}
// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Player.Api.Data.Data.Models;
using Player.Api.ViewModels;

namespace Player.Api.Infrastructure.Mappings
{
    public class FileProfile : AutoMapper.Profile
    {
        public FileProfile()
        {
            // The rest serve the Views export and import and move to Mapperly with Views. FileService maps
            // through FileMapper. The ReverseMap is kept because it also unflattens viewId onto View.Id.
            CreateMap<FileEntity, FileModel>().ReverseMap();
            CreateMap<FileEntity, FileDTO>();
            CreateMap<FileDTO, FileModel>();
        }
    }
}
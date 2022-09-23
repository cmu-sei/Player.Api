// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Player.Api.Data.Data.Models;
using Player.Api.ViewModels;

namespace Player.Api.Infrastructure.Mappings
{
    public class FileProfile : AutoMapper.Profile
    {
        public FileProfile()
        {
            CreateMap<FileForm, FileEntity>();
            CreateMap<FileEntity, FileModel>();
        }
    }
}
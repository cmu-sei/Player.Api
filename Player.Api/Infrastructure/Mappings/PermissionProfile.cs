// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Player.Api.Data.Data.Models;
using Player.Api.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace Player.Api.Infrastructure.Mappings
{
    public class PermissionProfile : AutoMapper.Profile
    {
        public PermissionProfile()
        {
            CreateMap<PermissionEntity, Permission>();
            CreateMap<PermissionForm, PermissionEntity>();

            CreateMap<UserEntity, UserPermissions>()
                .ForMember(dest => dest.RolePermissions, opt => opt.MapFrom(src =>
                    src.Role.Permissions.Select(x => x.Permission)))
                .ForMember(dest => dest.AssignedPermissions, opt => opt.MapFrom(src =>
                    src.Permissions.Select(x => x.Permission)))
                .ForMember(dest => dest.TeamPermissions, opt => opt.MapFrom(src => src.TeamMemberships));

            CreateMap<TeamMembershipEntity, TeamPermissions>()
                .ForMember(dest => dest.ViewId, opt => opt.MapFrom(src => src.ViewMembership.ViewId))
                .ForMember(dest => dest.IsPrimary, opt => opt.MapFrom(src => src.ViewMembership.PrimaryTeamMembershipId == src.Id))
                .ForMember(dest => dest.RolePermissions, opt => opt.MapFrom(src =>
                    src.Role.Permissions.Select(x => x.Permission)))
                .ForMember(dest => dest.TeamRolePermissions, opt => opt.MapFrom(src =>
                    src.Team.Role.Permissions.Select(x => x.Permission)))
                .ForMember(dest => dest.TeamAssignedPermissions, opt => opt.MapFrom(src =>
                    src.Team.Permissions.Select(x => x.Permission)));
        }
    }
}

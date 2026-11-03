// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Player.Api.Data.Data.Models;
using System.Linq;

namespace Player.Api.Features.Teams;

/// <summary>
/// The Teams maps still on AutoMapper. The rest of the feature maps through <see cref="TeamMapper"/>.
/// </summary>
/// <remarks>
/// These two are not used by the Teams feature. The Views export and import maps (ViewEntity to
/// ViewExportDTO and ViewExport to ViewEntity) use them for the nested teams, so they move to
/// Mapperly together with the Views feature.
/// </remarks>
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<TeamEntity, TeamExport>()
            .ForMember(dest => dest.RoleName, opt => opt.MapFrom(src => src.Role.Name))
            .ForMember(dest => dest.Permissions, opt => opt.MapFrom(src => src.Permissions.Select(x => x.Permission)))
            .ForMember(dest => dest.ScopedTeamIds, opt => opt.MapFrom(src => src.Scopes.Select(x => x.TargetTeamId)));
        CreateMap<TeamExport, TeamEntity>();
    }
}

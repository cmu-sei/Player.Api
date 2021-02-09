// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;

namespace Player.Api.Data.Data.Extensions
{
    public static class DirectoryExtensions
    {
        public static IQueryable<UserEntity> IncludePermissions(this IQueryable<UserEntity> query)
        {
            return query
                .Include(u => u.Role)
                    .ThenInclude(r => r.Permissions)
                        .ThenInclude(p => p.Permission)
                .Include(u => u.Permissions)
                    .ThenInclude(p => p.Permission);
        }
    }
}
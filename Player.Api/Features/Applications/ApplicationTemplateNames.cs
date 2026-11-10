// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Player.Api.Data.Data.Models;

namespace Player.Api.Features.Applications;

/// <summary>
/// Application Template names are compared ignoring case and surrounding spaces. Views are imported and
/// seeded by matching templates by name, so two templates with the same name make that match ambiguous,
/// and the admin lists cannot tell them apart.
/// </summary>
public static class ApplicationTemplateNames
{
    public static string Normalize(string name) => name?.Trim().ToLowerInvariant();

    public static bool SameName(string a, string b) =>
        !string.IsNullOrWhiteSpace(a) && Normalize(a) == Normalize(b);

    public static async Task<bool> IsTakenAsync(
        this IQueryable<ApplicationTemplateEntity> templates,
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Compared in memory, not in SQL, so the rule is the same as SameName on every database
        // provider (SQL trim and lower differ from .NET's on whitespace and non-ASCII letters).
        var others = await templates
            .Where(x => x.Id != exceptId)
            .Select(x => x.Name)
            .ToListAsync(cancellationToken);

        return others.Any(x => SameName(x, name));
    }

    public static string TakenMessage(string name) =>
        $"An Application Template named {name.Trim()} already exists.";
}

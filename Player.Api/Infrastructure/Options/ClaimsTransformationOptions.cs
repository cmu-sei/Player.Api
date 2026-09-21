// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Player.Api.Options
{
    public class ClaimsTransformationOptions
    {
        public bool EnableCaching { get; set; }
        public double CacheExpirationSeconds { get; set; }
        public bool UseRolesFromIdP { get; set; }
        public string RolesClaimPath { get; set; }
        public List<UserAttributeOptions> UserAttributes { get; set; } = [];

        public IReadOnlyList<UserAttributeDefinition> GetUserAttributeDefinitions()
        {
            var names = new HashSet<string>();

            return (UserAttributes ?? [])
                .Select((options, displayOrder) => new { options, displayOrder })
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.options.Name) &&
                    !string.IsNullOrWhiteSpace(item.options.ClaimPath) &&
                    names.Add(item.options.Name))
                .Select(item => new UserAttributeDefinition(
                    item.options.Name,
                    item.options.ClaimPath,
                    item.displayOrder))
                .ToArray();
        }
    }

    public class UserAttributeOptions
    {
        public string Name { get; set; }
        public string ClaimPath { get; set; }
    }

    public record UserAttributeDefinition(string Name, string ClaimPath, int DisplayOrder);
}

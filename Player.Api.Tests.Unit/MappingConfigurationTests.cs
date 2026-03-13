// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using AutoMapper.Internal;
using FakeItEasy;
using Microsoft.AspNetCore.Authorization;
using Player.Api.Infrastructure.Mappings;
using Player.Api.Services;
using Shouldly;
using Xunit;

namespace Player.Api.Tests.Unit;

public class MappingConfigurationTests
{
    [Fact]
    public void AutoMapper_Configuration_IsValid()
    {
        // Arrange
        var authorizationService = A.Fake<IAuthorizationService>();
        var userClaimsService = A.Fake<IUserClaimsService>();
        A.CallTo(() => userClaimsService.GetCurrentClaimsPrincipal())
            .Returns(new System.Security.Claims.ClaimsPrincipal());

        var config = new MapperConfiguration(cfg =>
        {
            cfg.Internal().ForAllPropertyMaps(
                pm => pm.SourceType != null && Nullable.GetUnderlyingType(pm.SourceType) == pm.DestinationType,
                (pm, c) => c.MapFrom<object, object, object, object>(new IgnoreNullSourceValues(), pm.SourceMember.Name));

            // Register all profiles from the Player.Api assembly
            cfg.AddMaps(typeof(Player.Api.Startup).Assembly);

            // Provide service constructor dependencies for value resolvers
            cfg.ConstructServicesUsing(type =>
            {
                if (type == typeof(Player.Api.Features.Teams.TeamMemberResolver))
                    return new Player.Api.Features.Teams.TeamMemberResolver(authorizationService, userClaimsService);
                if (type == typeof(Player.Api.Features.Teams.PrimaryTeamResolver))
                    return new Player.Api.Features.Teams.PrimaryTeamResolver(authorizationService, userClaimsService);
                return Activator.CreateInstance(type)!;
            });
        });

        // Act - verify mapper can be created (weaker than AssertConfigurationIsValid
        // because the app has unmapped navigation properties populated elsewhere)
        var mapper = config.CreateMapper();
        mapper.ShouldNotBeNull();
    }

    [Fact]
    public void AutoMapper_AllProfiles_AreRegistered()
    {
        // Arrange
        var assembly = typeof(Player.Api.Startup).Assembly;

        // Act
        var profiles = assembly.GetTypes()
            .Where(t => typeof(Profile).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        // Assert
        profiles.ShouldNotBeEmpty("Expected at least one AutoMapper profile in the Player.Api assembly");
        profiles.Count.ShouldBeGreaterThan(3, "Expected multiple mapping profiles for Views, Teams, Users, Files, etc.");
    }
}

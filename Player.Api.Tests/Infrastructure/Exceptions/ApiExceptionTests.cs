// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Player.Api.Data.Data.Models;
using Player.Api.Infrastructure.Exceptions;

namespace Player.Api.Tests.Infrastructure.Exceptions;

/// <summary>
/// The exceptions handlers throw to choose a response status. The status and the message are the whole
/// contract — <see cref="ExceptionMiddlewareTests"/> covers how they are turned into a response — so a
/// changed default message changes what an API client sees.
/// </summary>
public class ApiExceptionTests
{
    [Fact]
    public void A_conflict_is_a_409_and_says_what_kind_of_conflict()
    {
        var exception = new ConflictException();

        Assert.Equal(HttpStatusCode.Conflict, exception.GetStatusCode());
        Assert.Equal("Request would create conflict with current server state.", exception.Message);
    }

    [Fact]
    public void A_forbidden_request_is_a_403()
    {
        var exception = new ForbiddenException();

        Assert.Equal(HttpStatusCode.Forbidden, exception.GetStatusCode());
        Assert.Equal("Insufficient Permissions", exception.Message);
    }

    [Fact]
    public void A_missing_entity_is_a_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, new EntityNotFoundException<ViewEntity>().GetStatusCode());
    }

    /// <summary>
    /// With no message the entity type names itself, split into words — the message reaches the client as
    /// the problem title, so "View Entity not found" rather than "ViewEntityNotFound".
    /// </summary>
    [Theory]
    [InlineData(typeof(ViewEntity), "View Entity not found")]
    [InlineData(typeof(TeamMembershipEntity), "Team Membership Entity not found")]
    public void An_entity_not_found_message_is_built_from_the_type_name(Type entity, string expected)
    {
        var exception = (Exception)Activator.CreateInstance(
            typeof(EntityNotFoundException<>).MakeGenericType(entity));

        Assert.Equal(expected, exception.Message);
    }

    [Theory]
    [InlineData(typeof(ConflictException))]
    [InlineData(typeof(ForbiddenException))]
    [InlineData(typeof(EntityNotFoundException<ViewEntity>))]
    public void An_explicit_message_replaces_the_default(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "That view belongs to a parent.");

        Assert.Equal("That view belongs to a parent.", exception.Message);
    }
}

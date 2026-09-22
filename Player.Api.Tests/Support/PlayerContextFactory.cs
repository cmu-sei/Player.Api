// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Crucible.Common.EntityEvents.Interceptors;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Player.Api.Data.Data;

namespace Player.Api.Tests.Support;

/// <summary>
/// Builds <see cref="PlayerContext"/> instances wired the way production wires them.
/// </summary>
/// <remarks>
/// <see cref="PlayerContext"/> extends <c>EventPublishingDbContext</c>, and its
/// <c>PublishEventsAsync</c> resolves both <see cref="IMediator"/> and
/// <c>ILogger&lt;PlayerContext&gt;</c> off the settable <c>ServiceProvider</c> property using
/// <c>GetRequiredService</c>. Both must be registered or the first event-publishing save throws.
/// </remarks>
internal static class PlayerContextFactory
{
    /// <summary>
    /// Creates the service provider a session shares across all of its contexts, along with the
    /// substituted mediator tests assert on.
    /// </summary>
    public static (IServiceProvider Services, IMediator Mediator) CreateServices()
    {
        var mediator = Substitute.For<IMediator>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mediator);

        return (services.BuildServiceProvider(), mediator);
    }

    /// <summary>
    /// Creates a context for the given provider configuration, with the entity event interceptor
    /// attached so SaveChanges publishes events exactly as it does in production.
    /// </summary>
    public static PlayerContext CreateContext(
        Action<DbContextOptionsBuilder<PlayerContext>> configureProvider,
        IServiceProvider services)
    {
        var builder = new DbContextOptionsBuilder<PlayerContext>();
        configureProvider(builder);
        builder.AddInterceptors(new EntityEventInterceptor(NullLogger<EntityEventInterceptor>.Instance));

        return new PlayerContext(builder.Options) { ServiceProvider = services };
    }
}

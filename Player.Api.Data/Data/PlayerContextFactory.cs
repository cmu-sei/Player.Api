// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore;

namespace Player.Api.Data.Data;

public class PlayerContextFactory : IDbContextFactory<PlayerContext>
{
    private readonly IDbContextFactory<PlayerContext> _pooledFactory;
    private readonly IServiceProvider _serviceProvider;

    public PlayerContextFactory(
        IDbContextFactory<PlayerContext> pooledFactory,
        IServiceProvider serviceProvider)
    {
        _pooledFactory = pooledFactory;
        _serviceProvider = serviceProvider;
    }

    public PlayerContext CreateDbContext()
    {
        var context = _pooledFactory.CreateDbContext();

        // Inject the current scope's ServiceProvider
        context.ServiceProvider = _serviceProvider;
        return context;
    }
}
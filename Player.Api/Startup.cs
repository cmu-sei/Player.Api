// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Security.Principal;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Player.Api.Data.Data;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Authorization;
using Player.Api.Infrastructure.ClaimsTransformers;
using Player.Api.Infrastructure.Extensions;
using Player.Api.Infrastructure.Filters;
using Player.Api.Infrastructure.Mappings;
using Player.Api.Options;
using Player.Api.Services;
using Player.Api.Infrastructure.DbInterceptors;
using Microsoft.IdentityModel.JsonWebTokens;
using AutoMapper.Internal;

namespace Player.Api;

public class Startup
{
    private readonly Options.AuthorizationOptions _authOptions = new();
    private readonly SignalROptions _signalROptions = new();
    private IConfiguration Configuration { get; }
    private const string _routePrefix = "api";
    private string _pathbase;

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
        Configuration.GetSection("Authorization").Bind(_authOptions);
        Configuration.GetSection("SignalR").Bind(_signalROptions);
        _pathbase = Configuration["PathBase"];
    }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        // Add Azure Application Insights, if connection string is supplied
        string appInsights = Configuration["ApplicationInsights:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(appInsights))
        {
            services.AddApplicationInsightsTelemetry();
        }

        var provider = Configuration["Database:Provider"];
        switch (provider)
        {
            case "InMemory":
                services.AddPooledDbContextFactory<PlayerContext>((serviceProvider, opt) => opt
                        .AddInterceptors(serviceProvider.GetRequiredService<EventInterceptor>())
                        .UseInMemoryDatabase("api"));
                break;
            case "Sqlite":
            case "SqlServer":
            case "PostgreSQL":
                services.AddPooledDbContextFactory<PlayerContext>((serviceProvider, builder) => builder
                    .AddInterceptors(serviceProvider.GetRequiredService<EventInterceptor>())
                    .UseConfiguredDatabase(Configuration));
                break;
        }
        var connectionString = Configuration.GetConnectionString(DatabaseExtensions.DbProvider(Configuration));
        switch (provider)
        {
            case "Sqlite":
                services.AddHealthChecks().AddSqlite(connectionString, tags: new[] { "ready", "live" });
                break;
            case "SqlServer":
                services.AddHealthChecks().AddSqlServer(connectionString, tags: new[] { "ready", "live" });
                break;
            case "PostgreSQL":
                services.AddHealthChecks().AddNpgSql(connectionString, tags: new[] { "ready", "live" });
                break;
        }

        services.AddScoped<PlayerContextFactory>();
        services.AddScoped(sp => sp.GetRequiredService<PlayerContextFactory>().CreateDbContext());


        services.AddOptions()
            .Configure<DatabaseOptions>(Configuration.GetSection("Database"))
                .AddScoped(config => config.GetService<IOptionsMonitor<DatabaseOptions>>().CurrentValue)

            .Configure<ClaimsTransformationOptions>(Configuration.GetSection("ClaimsTransformation"))
                .AddScoped(config => config.GetService<IOptionsMonitor<ClaimsTransformationOptions>>().CurrentValue)

            .Configure<SeedDataOptions>(Configuration.GetSection("SeedData"))
                .AddScoped(config => config.GetService<IOptionsMonitor<SeedDataOptions>>().CurrentValue)

            .Configure<FileUploadOptions>(Configuration.GetSection("FileUpload"))
                .AddScoped(config => config.GetService<IOptionsMonitor<FileUploadOptions>>().CurrentValue)

            .Configure<Player.Api.Options.AuthorizationOptions>(Configuration.GetSection("Authorization"))
                .AddSingleton(config => config.GetService<IOptionsMonitor<Player.Api.Options.AuthorizationOptions>>().CurrentValue);

        services.AddCors(options => options.UseConfiguredCors(Configuration.GetSection("CorsPolicy")));

        services.AddSignalR(o => o.StatefulReconnectBufferSize = _signalROptions.StatefulReconnectBufferSizeBytes)
        .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        services.AddMvc(options =>
        {
            options.Filters.Add(typeof(ValidateModelStateFilter));
            options.Filters.Add(typeof(JsonExceptionFilter));
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddSwagger(_authOptions);

        JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = _authOptions.Authority;
            options.RequireHttpsMetadata = _authOptions.RequireHttpsMetadata;
            options.SaveToken = true;

            string[] validAudiences;

            if (_authOptions.ValidAudiences != null && _authOptions.ValidAudiences.Any())
            {
                validAudiences = _authOptions.ValidAudiences;
            }
            else
            {
                validAudiences = _authOptions.AuthorizationScope.Split(' ');
            }

            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateAudience = _authOptions.ValidateAudience,
                ValidAudiences = validAudiences
            };
        });

        services.AddRouting(options =>
        {
            options.LowercaseUrls = true;
        });

        services.AddMemoryCache();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Startup>());
        services.AddTransient<EventInterceptor>();

        services.AddScoped<IViewService, ViewService>();
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IViewMembershipService, ViewMembershipService>();
        services.AddScoped<ITeamMembershipService, TeamMembershipService>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<IPresenceService, PresenceService>();
        services.AddScoped<IWebhookService, WebhookService>();

        services.AddScoped<IClaimsTransformation, AuthorizationClaimsTransformer>();
        services.AddScoped<IUserClaimsService, UserClaimsService>();

        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IPrincipal>(p => p.GetService<IHttpContextAccessor>().HttpContext.User);

        services.AddSingleton<ConnectionCacheService>();
        services.AddSingleton<BackgroundWebhookService>();
        services.AddSingleton<IHostedService>(x => x.GetService<BackgroundWebhookService>());
        services.AddSingleton<IBackgroundWebhookService>(x => x.GetService<BackgroundWebhookService>());
        services.AddHttpClient();

        ApplyPolicies(services);

        services.AddAutoMapper(cfg =>
        {
            cfg.Internal().ForAllPropertyMaps(
                pm => pm.SourceType != null && Nullable.GetUnderlyingType(pm.SourceType) == pm.DestinationType,
                (pm, c) => c.MapFrom<object, object, object, object>(new IgnoreNullSourceValues(), pm.SourceMember.Name));
        }, typeof(Startup));
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UsePathBase(_pathbase);

        app.UseRouting();
        app.UseCors("default");

        //move any querystring jwt to Auth bearer header
        app.Use(async (context, next) =>
        {
            if (string.IsNullOrWhiteSpace(context.Request.Headers["Authorization"])
                && context.Request.QueryString.HasValue)
            {
                string token = context.Request.QueryString.Value
                    .Substring(1)
                    .Split('&')
                    .SingleOrDefault(x => x.StartsWith("bearer="))?.Split('=')[1];

                if (!String.IsNullOrWhiteSpace(token))
                    context.Request.Headers.Append("Authorization", new[] { $"Bearer {token}" });
            }

            await next.Invoke();

        });

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.RoutePrefix = _routePrefix;
            c.SwaggerEndpoint($"{_pathbase}/swagger/v1/swagger.json", "Player v1");
            c.OAuthClientId(_authOptions.ClientId);
            c.OAuthClientSecret(_authOptions.ClientSecret);
            c.OAuthAppName(_authOptions.ClientName);
            c.OAuthUsePkce();
        });

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks($"/{_routePrefix}/health/ready", new HealthCheckOptions()
                {
                    Predicate = (check) => check.Tags.Contains("ready"),
                });

                endpoints.MapHealthChecks($"/{_routePrefix}/health/live", new HealthCheckOptions()
                {
                    Predicate = (check) => check.Tags.Contains("live"),
                });

                endpoints.MapHub<Hubs.ViewHub>("/hubs/view", options =>
                {
                    options.AllowStatefulReconnects = _signalROptions.EnableStatefulReconnect;
                });
                endpoints.MapHub<Hubs.TeamHub>("/hubs/team", options =>
                {
                    options.AllowStatefulReconnects = _signalROptions.EnableStatefulReconnect;
                });
                endpoints.MapHub<Hubs.UserHub>("/hubs/user", options =>
                {
                    options.AllowStatefulReconnects = _signalROptions.EnableStatefulReconnect;
                });
            }
        );
    }


    private void ApplyPolicies(IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Require all scopes in authOptions
            var policyBuilder = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();
            Array.ForEach(_authOptions.AuthorizationScope.Split(' '), x => policyBuilder.RequireClaim("scope", x));

            options.DefaultPolicy = policyBuilder.Build();
        });

        // TODO: Add these automatically with reflection?
        services.AddSingleton<IAuthorizationHandler, FullRightsHandler>();
        services.AddSingleton<IAuthorizationHandler, ViewAdminHandler>();
        services.AddSingleton<IAuthorizationHandler, TeamAccessHandler>();
        services.AddSingleton<IAuthorizationHandler, SameUserOrViewAdminHandler>();
        services.AddSingleton<IAuthorizationHandler, SameUserHandler>();
        services.AddSingleton<IAuthorizationHandler, ViewMemberHandler>();
        services.AddSingleton<IAuthorizationHandler, ViewCreationHandler>();
        services.AddSingleton<IAuthorizationHandler, ManageTeamHandler>();
        services.AddSingleton<IAuthorizationHandler, TeamMemberHandler>();
        services.AddSingleton<IAuthorizationHandler, TeamsMemberHandler>();
        services.AddSingleton<IAuthorizationHandler, PrimaryTeamHandler>();
        services.AddSingleton<IAuthorizationHandler, ManageViewHandler>();
        services.AddScoped<IAuthorizationHandler, UserAccessHandler>();
    }
}
// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license, please see LICENSE.md in the project root for license information or contact permission@sei.cmu.edu for full terms.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Player.Api.Data.Data;
using Player.Api.Data.Data.Models;
using Player.Api.Extensions;
using Player.Api.Infrastructure.Options;
using TinCan;

namespace Player.Api.Services;

public interface IXApiService
{
    bool IsConfigured();
    Task EmitViewViewedAsync(Guid viewId, CancellationToken ct = default);
    Task EmitApplicationSwitchedAsync(Guid viewId, string applicationName, string applicationUrl, CancellationToken ct = default);
    Task EmitTeamJoinedAsync(Guid teamId, Guid viewId, CancellationToken ct = default);
    Task EmitTeamSwitchedAsync(Guid viewId, Guid teamId, CancellationToken ct = default);
    Task EmitViewTerminatedAsync(Guid viewId, TimeSpan duration, CancellationToken ct = default);
}

public class XApiService : IXApiService
{
    private readonly IDbContextFactory<PlayerContext> _contextFactory;
    private readonly ClaimsPrincipal _user;
    private readonly XApiOptions _xApiOptions;
    private readonly IXApiQueueService _queueService;
    private readonly ILogger<XApiService> _logger;
    private Agent _agent;

    public XApiService(
        IDbContextFactory<PlayerContext> contextFactory,
        IPrincipal user,
        XApiOptions xApiOptions,
        IXApiQueueService queueService,
        ILogger<XApiService> logger)
    {
        _contextFactory = contextFactory;
        _user = user as ClaimsPrincipal;
        _xApiOptions = xApiOptions;
        _queueService = queueService;
        _logger = logger;
    }

    private async Task EnsureAgentInitializedAsync(CancellationToken ct = default)
    {
        if (_agent != null || !IsConfigured())
            return;

        using var context = await _contextFactory.CreateDbContextAsync(ct);

        var account = new AgentAccount
        {
            name = _user.Identities.First().Claims.First(c => c.Type == "sub")?.Value
        };

        var iss = _user.Identities.First().Claims.First(c => c.Type == "iss")?.Value;
        if (!string.IsNullOrWhiteSpace(_xApiOptions.IssuerUrl))
        {
            account.homePage = new Uri(_xApiOptions.IssuerUrl);
        }
        else if (iss.Contains("http"))
        {
            account.homePage = new Uri(iss);
        }
        else
        {
            account.homePage = new Uri("http://" + iss);
        }

        var userId = _user.GetId();
        var user = await context.Users
            .Where(u => u.Id == userId)
            .FirstOrDefaultAsync(ct);

        _agent = new Agent
        {
            name = user?.Name ?? "Unknown User",
            account = account
        };
    }

    public bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(_xApiOptions.Username);
    }

    private Context BuildContext(Guid viewId, Guid? teamId = null)
    {
        var context = new Context
        {
            registration = viewId,
            platform = _xApiOptions.Platform,
            language = "en-US"
        };

        var contextActivities = new ContextActivities
        {
            category = new List<Activity>
            {
                new Activity
                {
                    id = "https://crucible.sei.cmu.edu/xapi/profile/v1"
                }
            }
        };

        context.contextActivities = contextActivities;

        // Add team context extension if teamId provided
        if (teamId.HasValue)
        {
            context.extensions = new TinCan.Extensions(
                new Newtonsoft.Json.Linq.JObject
                {
                    ["https://crucible.sei.cmu.edu/xapi/extensions/team"] = teamId.Value.ToString()
                });
        }

        return context;
    }

    public async Task EmitViewViewedAsync(Guid viewId, CancellationToken ct = default)
    {
        _logger.LogInformation("EmitViewViewedAsync called for View {ViewId}, IsConfigured: {IsConfigured}", viewId, IsConfigured());

        if (!IsConfigured())
        {
            _logger.LogWarning("xAPI is not configured - skipping statement emission");
            return;
        }

        try
        {
            await EnsureAgentInitializedAsync(ct);

            using var context = await _contextFactory.CreateDbContextAsync(ct);
            var view = await context.Views.FindAsync(new object[] { viewId }, ct);
            if (view == null)
            {
                _logger.LogWarning("Cannot emit ViewViewed: View {ViewId} not found", viewId);
                return;
            }

            // Get user's active team for this view
            var userId = _user.GetId();
            var viewMembership = await context.ViewMemberships
                .Include(vm => vm.PrimaryTeamMembership)
                .FirstOrDefaultAsync(vm => vm.ViewId == viewId && vm.UserId == userId, ct);
            var teamId = viewMembership?.PrimaryTeamMembership?.TeamId;

            var verb = new Verb { id = new Uri("http://id.tincanapi.com/verb/viewed") };
            verb.display = new LanguageMap();
            verb.display.Add("en-US", "viewed");

            var activity = new Activity { id = $"{_xApiOptions.ApiUrl}/views/{viewId}" };
            activity.definition = new ActivityDefinition
            {
                type = new Uri("http://adlnet.gov/expapi/activities/simulation")
            };
            activity.definition.name = new LanguageMap();
            activity.definition.name.Add("en-US", view.Name ?? "Unnamed View");
            activity.definition.description = new LanguageMap();
            activity.definition.description.Add("en-US", view.Description ?? $"Exercise workspace for View {viewId}");

            var statement = new Statement
            {
                actor = _agent,
                verb = verb,
                target = activity,
                context = BuildContext(viewId, teamId)
            };

            await _queueService.EnqueueAsync(new XApiQueuedStatementEntity
            {
                StatementJson = statement.ToJSON(true),
                Verb = "viewed",
                ActivityId = activity.id,
                ViewId = viewId
            }, ct);

            _logger.LogInformation("Queued ViewViewed statement for View {ViewId}, Team {TeamId}", viewId, teamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit ViewViewed for View {ViewId}", viewId);
        }
    }

    public async Task EmitApplicationSwitchedAsync(Guid viewId, string applicationName, string applicationUrl, CancellationToken ct = default)
    {
        if (!IsConfigured()) return;

        try
        {
            await EnsureAgentInitializedAsync(ct);

            using var context = await _contextFactory.CreateDbContextAsync(ct);

            // Get user's active team for this view
            var userId = _user.GetId();
            var viewMembership = await context.ViewMemberships
                .Include(vm => vm.PrimaryTeamMembership)
                .FirstOrDefaultAsync(vm => vm.ViewId == viewId && vm.UserId == userId, ct);
            var teamId = viewMembership?.PrimaryTeamMembership?.TeamId;

            var appDisplayNames = new Dictionary<string, string>
            {
                ["cite"] = "CITE Risk Assessment",
                ["gallery"] = "Gallery Articles",
                ["steamfitter"] = "Steamfitter Tasks",
                ["vm"] = "VM Console",
                ["console"] = "Terminal Console",
                ["player"] = "Player Dashboard",
                ["admin"] = "Admin Panel"
            };

            var displayName = appDisplayNames.ContainsKey(applicationName?.ToLower() ?? "")
                ? appDisplayNames[applicationName.ToLower()]
                : applicationName ?? "Unknown Application";

            var verb = new Verb { id = new Uri("http://activitystrea.ms/schema/1.0/access") };
            verb.display = new LanguageMap();
            verb.display.Add("en-US", "accessed");

            var encodedAppName = HttpUtility.UrlEncode(applicationName?.ToLower() ?? "unknown");
            var activity = new Activity { id = $"{_xApiOptions.ApiUrl}/views/{viewId}/apps/{encodedAppName}" };
            activity.definition = new ActivityDefinition
            {
                type = new Uri("http://adlnet.gov/expapi/activities/module")
            };
            activity.definition.name = new LanguageMap();
            activity.definition.name.Add("en-US", displayName);

            var contextObj = BuildContext(viewId, teamId);

            // Add parent context activity (the View)
            var parentActivity = new Activity { id = $"{_xApiOptions.ApiUrl}/views/{viewId}" };
            parentActivity.definition = new ActivityDefinition
            {
                type = new Uri("http://adlnet.gov/expapi/activities/simulation")
            };
            contextObj.contextActivities.parent = new List<Activity> { parentActivity };

            var statement = new Statement
            {
                actor = _agent,
                verb = verb,
                target = activity,
                context = contextObj
            };

            await _queueService.EnqueueAsync(new XApiQueuedStatementEntity
            {
                StatementJson = statement.ToJSON(true),
                Verb = "accessed",
                ActivityId = activity.id,
                ViewId = viewId
            }, ct);

            _logger.LogInformation("Queued ApplicationSwitched statement for View {ViewId}, App {ApplicationName}, Team {TeamId}", viewId, applicationName, teamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit ApplicationSwitched for View {ViewId}, App {ApplicationName}", viewId, applicationName);
        }
    }

    public async Task EmitTeamJoinedAsync(Guid teamId, Guid viewId, CancellationToken ct = default)
    {
        if (!IsConfigured()) return;

        try
        {
            await EnsureAgentInitializedAsync(ct);

            using var context = await _contextFactory.CreateDbContextAsync(ct);
            var team = await context.Teams.FindAsync(new object[] { teamId }, ct);
            if (team == null)
            {
                _logger.LogWarning("Cannot emit TeamJoined: Team {TeamId} not found", teamId);
                return;
            }

            var verb = new Verb { id = new Uri("http://adlnet.gov/expapi/verbs/attended") };
            verb.display = new LanguageMap();
            verb.display.Add("en-US", "attended");

            var activity = new Activity { id = $"{_xApiOptions.ApiUrl}/teams/{teamId}" };
            activity.definition = new ActivityDefinition
            {
                type = new Uri("http://id.tincanapi.com/activitytype/team")
            };
            activity.definition.name = new LanguageMap();
            activity.definition.name.Add("en-US", team.Name ?? "Unnamed Team");

            var statement = new Statement
            {
                actor = _agent,
                verb = verb,
                target = activity,
                context = BuildContext(viewId)
            };

            await _queueService.EnqueueAsync(new XApiQueuedStatementEntity
            {
                StatementJson = statement.ToJSON(true),
                Verb = "attended",
                ActivityId = activity.id,
                ViewId = viewId
            }, ct);

            _logger.LogInformation("Queued TeamJoined statement for Team {TeamId}, View {ViewId}", teamId, viewId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit TeamJoined for Team {TeamId}, View {ViewId}", teamId, viewId);
        }
    }

    public async Task EmitViewTerminatedAsync(Guid viewId, TimeSpan duration, CancellationToken ct = default)
    {
        if (!IsConfigured()) return;

        try
        {
            await EnsureAgentInitializedAsync(ct);

            using var context = await _contextFactory.CreateDbContextAsync(ct);

            // Get user's active team for this view
            var userId = _user.GetId();
            var viewMembership = await context.ViewMemberships
                .Include(vm => vm.PrimaryTeamMembership)
                .FirstOrDefaultAsync(vm => vm.ViewId == viewId && vm.UserId == userId, ct);
            var teamId = viewMembership?.PrimaryTeamMembership?.TeamId;

            var verb = new Verb { id = new Uri("http://adlnet.gov/expapi/verbs/terminated") };
            verb.display = new LanguageMap();
            verb.display.Add("en-US", "terminated");

            var activity = new Activity { id = $"{_xApiOptions.ApiUrl}/views/{viewId}" };
            activity.definition = new ActivityDefinition
            {
                type = new Uri("http://adlnet.gov/expapi/activities/simulation")
            };

            var statement = new Statement
            {
                actor = _agent,
                verb = verb,
                target = activity,
                result = new Result
                {
                    duration = duration
                },
                context = BuildContext(viewId, teamId)
            };

            await _queueService.EnqueueAsync(new XApiQueuedStatementEntity
            {
                StatementJson = statement.ToJSON(true),
                Verb = "terminated",
                ActivityId = activity.id,
                ViewId = viewId
            }, ct);

            _logger.LogInformation("Queued ViewTerminated statement for View {ViewId}, Duration {Duration}, Team {TeamId}", viewId, duration, teamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit ViewTerminated for View {ViewId}", viewId);
        }
    }

    public async Task EmitTeamSwitchedAsync(Guid viewId, Guid teamId, CancellationToken ct = default)
    {
        if (!IsConfigured()) return;

        try
        {
            await EnsureAgentInitializedAsync(ct);

            using var context = await _contextFactory.CreateDbContextAsync(ct);
            var team = await context.Teams.FindAsync(new object[] { teamId }, ct);
            if (team == null)
            {
                _logger.LogWarning("Cannot emit TeamSwitched: Team {TeamId} not found", teamId);
                return;
            }

            var verb = new Verb { id = new Uri("https://w3id.org/xapi/verbs/switched") };
            verb.display = new LanguageMap();
            verb.display.Add("en-US", "switched");

            var activity = new Activity { id = $"{_xApiOptions.ApiUrl}/teams/{teamId}" };
            activity.definition = new ActivityDefinition
            {
                type = new Uri("http://id.tincanapi.com/activitytype/team")
            };
            activity.definition.name = new LanguageMap();
            activity.definition.name.Add("en-US", team.Name ?? "Unnamed Team");

            var contextObj = BuildContext(viewId, teamId);

            // Add parent context activity (the View)
            var parentActivity = new Activity { id = $"{_xApiOptions.ApiUrl}/views/{viewId}" };
            parentActivity.definition = new ActivityDefinition
            {
                type = new Uri("http://adlnet.gov/expapi/activities/simulation")
            };
            contextObj.contextActivities.parent = new List<Activity> { parentActivity };

            var statement = new Statement
            {
                actor = _agent,
                verb = verb,
                target = activity,
                context = contextObj
            };

            await _queueService.EnqueueAsync(new XApiQueuedStatementEntity
            {
                StatementJson = statement.ToJSON(true),
                Verb = "switched",
                ActivityId = activity.id,
                ViewId = viewId
            }, ct);

            _logger.LogInformation("Queued TeamSwitched statement for Team {TeamId}, View {ViewId}", teamId, viewId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit TeamSwitched for Team {TeamId}, View {ViewId}", teamId, viewId);
        }
    }
}

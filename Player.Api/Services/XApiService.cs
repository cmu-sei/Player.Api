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
    private readonly PlayerContext _context;
    private readonly ClaimsPrincipal _user;
    private readonly XApiOptions _xApiOptions;
    private readonly IXApiQueueService _queueService;
    private readonly ILogger<XApiService> _logger;
    private Agent _agent;

    public XApiService(
        PlayerContext context,
        IPrincipal user,
        XApiOptions xApiOptions,
        IXApiQueueService queueService,
        ILogger<XApiService> logger)
    {
        _context = context;
        _user = user as ClaimsPrincipal;
        _xApiOptions = xApiOptions;
        _queueService = queueService;
        _logger = logger;
    }

    private async Task EnsureAgentInitializedAsync(CancellationToken ct = default)
    {
        if (_agent != null || !IsConfigured())
            return;

        var account = new AgentAccount
        {
            Name = _user.Identities.First().Claims.First(c => c.Type == "sub")?.Value
        };

        var iss = _user.Identities.First().Claims.First(c => c.Type == "iss")?.Value;
        if (!string.IsNullOrWhiteSpace(_xApiOptions.IssuerUrl))
        {
            account.HomePage = new Uri(_xApiOptions.IssuerUrl);
        }
        else if (iss.Contains("http"))
        {
            account.HomePage = new Uri(iss);
        }
        else
        {
            account.HomePage = new Uri("http://" + iss);
        }

        var userId = _user.GetId();
        var user = await _context.Users
            .Where(u => u.Id == userId)
            .FirstOrDefaultAsync(ct);

        _agent = new Agent
        {
            Name = user?.Name ?? "Unknown User",
            Account = account
        };
    }

    public bool IsConfigured()
    {
        return _xApiOptions.Enabled && !string.IsNullOrWhiteSpace(_xApiOptions.Username);
    }

    private Context BuildContext(Guid viewId, TeamEntity team = null)
    {
        var context = new Context
        {
            Registration = viewId,
            Platform = _xApiOptions.Platform,
            Language = "en-US"
        };

        var contextActivities = new ContextActivities
        {
            Category = new List<Activity>
            {
                new Activity
                {
                    Id = "https://crucible.sei.cmu.edu/xapi/profile/v1"
                }
            }
        };

        context.ContextActivities = contextActivities;

        if (team != null)
        {
            var group = new Group
            {
                Name = team.Name ?? "Unnamed Team",
                Account = new AgentAccount
                {
                    HomePage = new Uri(_xApiOptions.UiUrl),
                    Name = team.Id.ToString()
                },
                Member = new List<Agent> { _agent }
            };
            context.Team = group;

            // Retain the existing extension for consumers that use it.
            context.Extensions = new TinCan.Extensions(
                new Newtonsoft.Json.Linq.JObject
                {
                    ["https://crucible.sei.cmu.edu/xapi/extensions/team"] = team.Id.ToString()
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

            var view = await _context.Views.FindAsync(new object[] { viewId }, ct);
            if (view == null)
            {
                _logger.LogWarning("Cannot emit ViewViewed: View {ViewId} not found", viewId);
                return;
            }

            // Get user's active team for this view
            var userId = _user.GetId();
            var viewMembership = await _context.ViewMemberships
                .Include(vm => vm.PrimaryTeamMembership)
                .ThenInclude(tm => tm.Team)
                .FirstOrDefaultAsync(vm => vm.ViewId == viewId && vm.UserId == userId, ct);
            var team = viewMembership?.PrimaryTeamMembership?.Team;

            var verb = new Verb { Id = new Uri("http://id.tincanapi.com/verb/viewed") };
            verb.Display = new LanguageMap();
            verb.Display.Add("en-US", "viewed");

            var activity = new Activity { Id = $"{_xApiOptions.ApiUrl}/views/{viewId}" };
            activity.Definition = new ActivityDefinition
            {
                Type = new Uri("http://adlnet.gov/expapi/activities/simulation")
            };
            activity.Definition.Name = new LanguageMap();
            activity.Definition.Name.Add("en-US", view.Name ?? "Unnamed View");
            activity.Definition.Description = new LanguageMap();
            activity.Definition.Description.Add("en-US", view.Description ?? $"Exercise workspace for View {viewId}");

            var statement = new Statement
            {
                Actor = _agent,
                Verb = verb,
                Target = activity,
                Context = BuildContext(viewId, team)
            };

            await _queueService.EnqueueAsync(new XApiQueuedStatementEntity
            {
                StatementJson = statement.ToJson(true),
                Verb = "viewed",
                ActivityId = activity.Id,
                ViewId = viewId
            }, ct);

            _logger.LogInformation("Queued ViewViewed statement for View {ViewId}, Team {TeamId}", viewId, team?.Id);
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

            // Get user's active team for this view
            var userId = _user.GetId();
            var viewMembership = await _context.ViewMemberships
                .Include(vm => vm.PrimaryTeamMembership)
                .ThenInclude(tm => tm.Team)
                .FirstOrDefaultAsync(vm => vm.ViewId == viewId && vm.UserId == userId, ct);
            var team = viewMembership?.PrimaryTeamMembership?.Team;

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

            var verb = new Verb { Id = new Uri("http://activitystrea.ms/schema/1.0/access") };
            verb.Display = new LanguageMap();
            verb.Display.Add("en-US", "accessed");

            var encodedAppName = HttpUtility.UrlEncode(applicationName?.ToLower() ?? "unknown");
            var activity = new Activity { Id = $"{_xApiOptions.ApiUrl}/views/{viewId}/apps/{encodedAppName}" };
            activity.Definition = new ActivityDefinition
            {
                Type = new Uri("http://adlnet.gov/expapi/activities/module")
            };
            activity.Definition.Name = new LanguageMap();
            activity.Definition.Name.Add("en-US", displayName);

            var contextObj = BuildContext(viewId, team);

            // Add parent context activity (the View)
            var parentActivity = new Activity { Id = $"{_xApiOptions.ApiUrl}/views/{viewId}" };
            parentActivity.Definition = new ActivityDefinition
            {
                Type = new Uri("http://adlnet.gov/expapi/activities/simulation")
            };
            contextObj.ContextActivities.Parent = new List<Activity> { parentActivity };

            var statement = new Statement
            {
                Actor = _agent,
                Verb = verb,
                Target = activity,
                Context = contextObj
            };

            await _queueService.EnqueueAsync(new XApiQueuedStatementEntity
            {
                StatementJson = statement.ToJson(true),
                Verb = "accessed",
                ActivityId = activity.Id,
                ViewId = viewId
            }, ct);

            _logger.LogInformation("Queued ApplicationSwitched statement for View {ViewId}, App {ApplicationName}, Team {TeamId}", viewId, applicationName, team?.Id);
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

            var team = await _context.Teams.FindAsync(new object[] { teamId }, ct);
            if (team == null)
            {
                _logger.LogWarning("Cannot emit TeamJoined: Team {TeamId} not found", teamId);
                return;
            }

            var verb = new Verb { Id = new Uri("http://adlnet.gov/expapi/verbs/attended") };
            verb.Display = new LanguageMap();
            verb.Display.Add("en-US", "attended");

            var activity = new Activity { Id = $"{_xApiOptions.ApiUrl}/teams/{teamId}" };
            activity.Definition = new ActivityDefinition
            {
                Type = new Uri("http://id.tincanapi.com/activitytype/team")
            };
            activity.Definition.Name = new LanguageMap();
            activity.Definition.Name.Add("en-US", team.Name ?? "Unnamed Team");

            var statement = new Statement
            {
                Actor = _agent,
                Verb = verb,
                Target = activity,
                Context = BuildContext(viewId, team)
            };

            await _queueService.EnqueueAsync(new XApiQueuedStatementEntity
            {
                StatementJson = statement.ToJson(true),
                Verb = "attended",
                ActivityId = activity.Id,
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

            // Get user's active team for this view
            var userId = _user.GetId();
            var viewMembership = await _context.ViewMemberships
                .Include(vm => vm.PrimaryTeamMembership)
                .ThenInclude(tm => tm.Team)
                .FirstOrDefaultAsync(vm => vm.ViewId == viewId && vm.UserId == userId, ct);
            var team = viewMembership?.PrimaryTeamMembership?.Team;

            var verb = new Verb { Id = new Uri("http://adlnet.gov/expapi/verbs/terminated") };
            verb.Display = new LanguageMap();
            verb.Display.Add("en-US", "terminated");

            var activity = new Activity { Id = $"{_xApiOptions.ApiUrl}/views/{viewId}" };
            activity.Definition = new ActivityDefinition
            {
                Type = new Uri("http://adlnet.gov/expapi/activities/simulation")
            };

            var statement = new Statement
            {
                Actor = _agent,
                Verb = verb,
                Target = activity,
                Result = new Result
                {
                    Duration = duration
                },
                Context = BuildContext(viewId, team)
            };

            await _queueService.EnqueueAsync(new XApiQueuedStatementEntity
            {
                StatementJson = statement.ToJson(true),
                Verb = "terminated",
                ActivityId = activity.Id,
                ViewId = viewId
            }, ct);

            _logger.LogInformation("Queued ViewTerminated statement for View {ViewId}, Duration {Duration}, Team {TeamId}", viewId, duration, team?.Id);
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

            var team = await _context.Teams.FindAsync(new object[] { teamId }, ct);
            if (team == null)
            {
                _logger.LogWarning("Cannot emit TeamSwitched: Team {TeamId} not found", teamId);
                return;
            }

            var verb = new Verb { Id = new Uri("https://w3id.org/xapi/verbs/switched") };
            verb.Display = new LanguageMap();
            verb.Display.Add("en-US", "switched");

            var activity = new Activity { Id = $"{_xApiOptions.ApiUrl}/teams/{teamId}" };
            activity.Definition = new ActivityDefinition
            {
                Type = new Uri("http://id.tincanapi.com/activitytype/team")
            };
            activity.Definition.Name = new LanguageMap();
            activity.Definition.Name.Add("en-US", team.Name ?? "Unnamed Team");

            var contextObj = BuildContext(viewId, team);

            // Add parent context activity (the View)
            var parentActivity = new Activity { Id = $"{_xApiOptions.ApiUrl}/views/{viewId}" };
            parentActivity.Definition = new ActivityDefinition
            {
                Type = new Uri("http://adlnet.gov/expapi/activities/simulation")
            };
            contextObj.ContextActivities.Parent = new List<Activity> { parentActivity };

            var statement = new Statement
            {
                Actor = _agent,
                Verb = verb,
                Target = activity,
                Context = contextObj
            };

            await _queueService.EnqueueAsync(new XApiQueuedStatementEntity
            {
                StatementJson = statement.ToJson(true),
                Verb = "switched",
                ActivityId = activity.Id,
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

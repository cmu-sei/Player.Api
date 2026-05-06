// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Player.Api.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Player.Api.Controllers;

[Authorize]
[Route("api/xapi")]
[ApiController]
public class XApiController : ControllerBase
{
    private readonly IXApiService _xApiService;

    public XApiController(IXApiService xApiService)
    {
        _xApiService = xApiService;
    }

    /// <summary>
    /// Logs xAPI experienced statement for switching to an application
    /// </summary>
    /// <param name="viewId">The id of the View</param>
    /// <param name="applicationName">The name of the application</param>
    /// <param name="applicationUrl">The URL of the application</param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpPost("experienced/view/{viewId}/application")]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "applicationSwitched")]
    public async Task<IActionResult> ApplicationSwitched(
        Guid viewId,
        [FromQuery] string applicationName,
        [FromQuery] string applicationUrl,
        CancellationToken ct)
    {
        if (!_xApiService.IsConfigured())
            return Ok();

        await _xApiService.EmitApplicationSwitchedAsync(viewId, applicationName, applicationUrl, ct);
        return Ok();
    }

    /// <summary>
    /// Logs xAPI terminated statement for a View
    /// </summary>
    /// <param name="viewId">The id of the View</param>
    /// <param name="durationSeconds">Duration in seconds</param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpPost("terminated/view/{viewId}")]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "viewTerminated")]
    public async Task<IActionResult> ViewTerminated(
        Guid viewId,
        [FromQuery] int durationSeconds,
        CancellationToken ct)
    {
        if (!_xApiService.IsConfigured())
            return Ok();

        var duration = TimeSpan.FromSeconds(durationSeconds);
        await _xApiService.EmitViewTerminatedAsync(viewId, duration, ct);
        return Ok();
    }

    /// <summary>
    /// Logs xAPI statement when user switches their active team
    /// </summary>
    /// <param name="viewId">The id of the View</param>
    /// <param name="teamId">The id of the Team</param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpPost("switched/view/{viewId}/team/{teamId}")]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "teamSwitched")]
    public async Task<IActionResult> TeamSwitched(
        Guid viewId,
        Guid teamId,
        CancellationToken ct)
    {
        if (!_xApiService.IsConfigured())
            return Ok();

        await _xApiService.EmitTeamSwitchedAsync(viewId, teamId, ct);
        return Ok();
    }
}

// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Player.Api.Services;
using Player.Api.ViewModels.Webhooks;
using Swashbuckle.AspNetCore.Annotations;

namespace Player.Api.Controllers
{
    public class WebhookController : BaseController
    {
        private readonly IAuthorizationService _authorizationService;
        private readonly IWebhookService _webhookService;

        public WebhookController(IAuthorizationService authorizationService, IWebhookService webhookService)
        {
            _authorizationService = authorizationService;
            _webhookService = webhookService;
        }

        /// <summary>
        /// Returns all subscriptions in the system
        /// </summary>
        [HttpGet("webhooks")]
        [ProducesResponseType(typeof(IEnumerable<WebhookSubscription>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getAll")]
        public async Task<IActionResult> GetAction(CancellationToken ct)
        {
            var subs = await _webhookService.GetAll(ct);
            return Ok(subs);
        }

        /// <summary>
        /// Subscribes to an event in the Player API
        /// </summary>
        [HttpPost("webhooks/subscribe")]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "subscribe")]
        public async Task<IActionResult> Subscribe ([FromBody] WebhookSubscription form, CancellationToken ct)
        {
            await _webhookService.Subscribe(form, ct);
            return Ok();
        }
    }
}
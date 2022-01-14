// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Player.Api.Services;
using Player.Api.ViewModels.Webhooks;
using Swashbuckle.AspNetCore.Annotations;
using System;

namespace Player.Api.Controllers
{
    public class WebhookController : BaseController
    {
        private readonly IWebhookService _webhookService;

        public WebhookController(IWebhookService webhookService)
        {
            _webhookService = webhookService;
        }

        /// <summary>
        /// Returns all subscriptions in the system
        /// </summary>
        [HttpGet("webhooks")]
        [ProducesResponseType(typeof(IEnumerable<WebhookSubscription>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "getAllWebhooks")]
        public async Task<IActionResult> GetAction(CancellationToken ct)
        {
            var subs = await _webhookService.GetAll(ct);
            return Ok(subs);
        }

        /// <summary>
        /// Subscribes to an event in the Player API
        /// </summary>
        [HttpPost("webhooks/subscribe")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "createWebhookSubscription")]
        public async Task<IActionResult> Subscribe([FromBody] WebhookSubscriptionForm form, CancellationToken ct)
        {
            await _webhookService.Subscribe(form, ct);
            return Ok();
        }

        /// <summary>
        /// Deletes the subscription with the given id
        /// </summary>
        /// <param name="id">The Id of the subscription to delete</param>
        /// <param name="ct"></param>
        [HttpDelete("webhooks/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "deleteWebhookSubscription")]
        public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
        {
            await _webhookService.DeleteAsync(id, ct);
            return NoContent();
        }

        /// <summary>
        /// Updates a subscription
        /// </summary>
        /// <param name="id">The Id of the subscription to update</param>
        /// <param name="form"></param>
        /// <param name="ct"></param>
        [HttpPut("webhooks/{id}")]
        [ProducesResponseType(typeof(WebhookSubscription), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "updateWebhookSubscription")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] WebhookSubscriptionForm form, CancellationToken ct)
        {
            var updated = await _webhookService.UpdateAsync(id, form, ct);
            return Ok(updated);
        }
    }
}
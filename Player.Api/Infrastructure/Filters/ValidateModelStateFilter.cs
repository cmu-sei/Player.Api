// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Player.Api.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Player.Api.Infrastructure.Filters
{
    public class ValidateModelStateFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var modelState = context.ModelState;

            if (!modelState.IsValid)
            {
                ProblemDetails error = new ProblemDetails {
                    Title = "Invalid Data",
                    Status = (int)System.Net.HttpStatusCode.BadRequest
                };

                List<string> errorDetails = modelState.Keys
                    .SelectMany(key => modelState[key].Errors.Select(x => $"{key}: { (string.IsNullOrEmpty(x.ErrorMessage) ? x.Exception.Message : x.ErrorMessage) }"))
                    .ToList();

                error.Detail = string.Join("\n", errorDetails.ToArray());

                context.Result = new BadRequestObjectResult(error);
            }
        }
    }
}

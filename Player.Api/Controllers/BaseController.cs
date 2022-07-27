// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Player.Api.Controllers
{
    [Authorize]
    [Route("api/")]
    [ApiController]
    public abstract class BaseController : ControllerBase
    {
    }

    public class EmptyBodyModelBinder<T> : IModelBinder
    {
        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var stream = bindingContext.HttpContext.Request.Body;
            using var reader = new StreamReader(stream);
            var jsonbody = (await reader.ReadToEndAsync()).Replace("\n", "");
            if (string.IsNullOrWhiteSpace(jsonbody))
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
            else
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                };
                var obj = JsonSerializer.Deserialize<T>(jsonbody, options);
                bindingContext.Result =  ModelBindingResult.Success(obj);
            }
        }
    }

}

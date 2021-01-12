// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Player.Api.ViewModels;
using System.Net;

namespace Player.Api.Controllers
{
    [Authorize]
    [Route("api/")]
    [ApiController]
    public abstract class BaseController : ControllerBase
    {
    }
}

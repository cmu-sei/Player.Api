// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Text.RegularExpressions;

namespace Player.Api.Infrastructure.Exceptions
{
    public class ConflictException : Exception, IApiException
    {
        public ConflictException()
            : base("Request would create conflict with current server state.")
        {
        }

        public ConflictException(string message)
            : base(message)
        {
        }

        public HttpStatusCode GetStatusCode()
        {
            return HttpStatusCode.Conflict;
        }
    }
}

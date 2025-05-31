// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Microsoft.AspNetCore.SignalR;
using Player.Api.Data.Data;
using Player.Api.Hubs;
using Player.Api.Infrastructure.Authorization;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Diagnostics;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Player.Api.Services
{
    public interface ITelemetryService
    {
    }

    public class TelemetryService : ITelemetryService
    {
        public  readonly Meter ViewUsersMeter = new Meter("cmu_sei_player_view_users", "1.0");
        public  Gauge<int> ViewActiveUsers;

        public TelemetryService()
        {
            ViewActiveUsers = ViewUsersMeter.CreateGauge<int>("player_view_active_users");
        }

    }
}

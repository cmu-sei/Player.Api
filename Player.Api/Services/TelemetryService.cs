// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Diagnostics.Metrics;

namespace Player.Api.Services
{
    public interface ITelemetryService
    {
    }

    public class TelemetryService : ITelemetryService
    {
        public const string ViewUsersMeterName = "player_view_users";
        public readonly Meter ViewUsersMeter = new Meter(ViewUsersMeterName, "1.0");
        public Gauge<int> ViewActiveUsers;

        public TelemetryService()
        {
            ViewActiveUsers = ViewUsersMeter.CreateGauge<int>("player_view_active_users");
        }

    }
}

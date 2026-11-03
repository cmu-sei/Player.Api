// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Riok.Mapperly.Abstractions;

// Defaults for every Mapperly [Mapper] in the application. Unmapped members are compiler warnings,
// and the build treats warnings as errors, so a member that stops being mapped fails the build.
[assembly: MapperDefaults(
    // Outbound maps (entity to view model) must fill every member of the view model, so a response
    // never silently carries a default. Inbound maps (command onto entity) ask the opposite question
    // instead, with [MapperRequiredMapping(RequiredMappingStrategy.Source)]: every command member must
    // reach the entity, while Id, navigation properties and server-assigned fields are left alone.
    RequiredMappingStrategy = RequiredMappingStrategy.Target,

    // A null source value is skipped rather than written when the target cannot hold null — int? to
    // int, Guid? to Guid — so the target keeps the value it already had. Every PATCH-style update
    // relies on this; it is the convention Startup installs for AutoMapper. False is Mapperly's
    // default, stated here so it cannot change unnoticed.
    ThrowOnPropertyMappingNullMismatch = false)]

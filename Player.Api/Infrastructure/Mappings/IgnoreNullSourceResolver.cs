// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;

namespace Player.Api.Infrastructure.Mappings
{
    class IgnoreNullSourceValues : IMemberValueResolver<object, object, object, object>
    {
        public object Resolve(object source, object destination, object sourceMember, object destinationMember, ResolutionContext context)
        {
            return sourceMember ?? destinationMember;
        }
    }
}
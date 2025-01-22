// Copyright 2022 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;

namespace Player.Api.Features.Applications
{
    public class ApplicationTemplate
    {
        public Guid Id { get; set; }

        /// <summary>
        /// The location of the application. {teamId}, {teamName}, {viewId} and {viewName} will be replaced dynamically if included
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The location of the application. {teamId}, {teamName}, {viewId} and {viewName} will be replaced dynamically if included
        /// </summary>
        [Url]
        public string Url { get; set; }

        public string Icon { get; set; }

        public bool Embeddable { get; set; }
        public bool LoadInBackground { get; set; }
    }

    public class Application
    {
        public Guid Id { get; set; }

        /// <summary>
        /// The location of the application. {teamId}, {teamName}, {viewId} and {viewName} will be replaced dynamically if included
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The location of the application. {teamId}, {teamName}, {viewId} and {viewName} will be replaced dynamically if included
        /// </summary>
        [Url]
        public string Url { get; set; }

        public string Icon { get; set; }

        public bool? Embeddable { get; set; }
        public bool? LoadInBackground { get; set; }

        [Required]
        public Guid ViewId { get; set; }

        public Guid? ApplicationTemplateId { get; set; }
    }

    public class ApplicationInstance
    {
        public Guid Id { get; set; }

        public Guid ApplicationId { get; set; }

        public float DisplayOrder { get; set; }

        /// <summary>
        /// The location of the application. {teamId}, {teamName}, {viewId} and {viewName} will be replaced dynamically if included
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The location of the application. {teamId}, {teamName}, {viewId} and {viewName} will be replaced dynamically if included
        /// </summary>
        public string Url { get; set; }

        public string Icon { get; set; }

        public bool Embeddable { get; set; }
        public bool LoadInBackground { get; set; }

        public Guid ViewId { get; set; }
    }
}

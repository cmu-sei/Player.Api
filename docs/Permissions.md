As of version 3.3.4, Player transitioned to a new permissions model, allowing for more granular access control to different features of the application. This document will detail how the new system works.

# Permissions

Access to features of Player are governed by sets of Permissions. Permissions can apply globally or on a per View basis. Examples of global Permissions are:

- CreateViews - Allows creation of new Views
- ViewViews - Allows viewing all Views and their Users and Groups
- ManageUsers - Allows for making changes to Users.

The Administration area now can be accessed by any User with View or Manage Permission to an Administration function (e.g. ViewApplications, ManageWorkspaces, etc), but only the areas they have Permissions for will be accessible in the sidebar menu.

There are many more Permissions available. They can be viewed by going to the new Roles section of the Administration area. Custom Permissions can be added through Seed Data or the API that other Crucible applications (such as the Vm API) can use as well. There are some default Permissions for this purpose, such as UploadVmFiles, ReverVms, etc.

# Roles

Permissions can be applied to Users by grouping them into Roles. There are two types of Roles in Player:

- System Roles - Each User can have a System Role applied to them that gives global Permissions across all of Player. The three default System Roles are:

  - Administrator - Has all Permissions within the system.
  - Content Developer - Has the `CreateViews` Permissions. Users in this Role can create and manage their own Views, but not affect any global settings or other User's Views.
  - Observer - Has all `View` Permissions. Users in this Role can view everything in the system, but not make any changes.

  Custom System Roles can be created by Users with the `ManageRoles` Permission that include whatever Permissions are desired for that Role. This can be done in the Roles section of the Administration area.

- Team Roles - When a User is added to a Team in a View, their Team Membership has a Team Role that applies Permissions to that User only for that specific Team. The default Team Roles are:

  - View Member - Can view and access all objects within the Team.
  - View Admin - Can perform all View actions across all Teams in the View, including managing User access to the View. When creating a new View, the creator is given the `View Admin` Role in that View.
  - Observer - Can view all objects within the View, but not many any changes.

  Custom Team Roles can be created if different Permissions combinations are needed.

Roles can be set on Users in the Users section of the Administration area.

Roles can also optionally be integrated with your Identity Provider. See Identity Provider Integration below.

# Seed Data

The SeedData section of appsettings.json has been changed to support the new model. You can now use this section to add Roles and Users on application startup. See appsettings.json for examples.

SeedData will only add objects if they do not exist. It will not modify existing Roles and Users so as not to undo changes made in the application on every restart. It will re-create objects if they are deleted in the application, so be sure to remove them from SeedData if they are no longer wanted.

# Identity Provider Integration

Roles can optionally be integrated with the Identity Provider that is being used to authenticate to Player. There are new settings under `ClaimsTransformation` to configure this integration. See appsettings.json. This integration is compatible with any Identity Provider that is capable of putting Roles into the auth token.

## Roles

If enabled, Roles from the User's auth token will be applied as if the Role was set on the User directly in Player. The Role must exist in Player and the name of the Role in the token must match exactly with the name of the Role in the token.

- UseRolesFromIdp: If true, Roles from the User's auth token will be used. Defaults to true.
- RolesClaimPath: The path within the User's auth token to look for Roles. Defaults to Keycloak's default value of `realm_access.roles`.

  Example: If the defaults are set, Player will apply the `Content Developer` Role to a User whose token contains the following:

```json
  realm_access {
    roles: [
        "Content Developer"
    ]
  }
```

If multiple Roles are present in the token, or if one Role is in the token and one Role is set directly on the User in Player, the Permissions of all of the Roles will be combined.

## Keycloak

If you are using Keycloak as your Identity Provider, Roles should work by default if you have not changed the default `RolesClaimPath`. You may need to adjust this value if your Keycloak is configured to put Roles in a different location within the token.

# Migration

When moving from a version prior to 3.3.4, the database will be migrated from the old Permissions sytem to the new one. The end result should be no change in access to any existing Users.

- Any existing Users with the old `SystemAdmin` Permission will be migrated to the new `Administrator` Role
- Any existing Teams with the old `ViewAdmin` Permissions will be migrated to the new `View Admin` Team Role
- Any existing Teams with the old `ReadOnly` Permissions will be migrated to the new `Observer` Team Role
- Any existing Teams with no old Permissions will be migrated to the new 'View Member' Team Role

Be sure to double check all of your Roles and Team Memberships once the migration is complete.

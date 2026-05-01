# WebApplication1

ASP.NET Core MVC restaurant application with Identity and Entity Framework Core.

## Current Scope

The project already includes:

- public restaurant website
- admin authentication and role seeding
- user management
- menu management
- table management
- ingredient management
- inventory management

## Architecture Reference

Detailed system analysis, target domain model, and implementation roadmap are documented in:

- `docs/restaurant-system-blueprint.md`

## Local Run

1. Set connection string in `appsettings.json`.
2. Set `ADMIN_DEFAULT_PASSWORD` via environment variable or user secrets.
3. Run the app with `dotnet run`.

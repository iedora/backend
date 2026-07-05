using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Auth.Data;

/// <summary>
/// The Identity store — full ASP.NET Core Identity schema (AspNetUsers, AspNetRoles,
/// AspNetUserRoles, ...) on Postgres, with Guid keys. EnsureCreated builds it on startup.
/// </summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options);

using Framework.Commands;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Kernel;

/// <summary>The 202 body for an accepted async write: the command id + where to poll its outcome.</summary>
public sealed record CommandAccepted(Guid CommandId, string StatusUrl);

/// <summary>The poll response for a command's outcome.</summary>
public sealed record CommandStatusResponse(Guid Id, string Status, string? ErrorCode, string? ResultLocation);

/// <summary>Shared helpers for the async-write pipeline's HTTP surface (kept in the web project so
/// Framework.Commands can stay non-web / worker-safe).</summary>
public static class CommandEndpoints
{
    /// <summary>Build the <c>202 Accepted</c> for a submitted command under the given route prefix
    /// (e.g. <c>"/tenancy"</c> → status at <c>/tenancy/commands/{id}</c>).</summary>
    public static Accepted<CommandAccepted> AcceptedCommand(Guid commandId, string prefix)
    {
        var url = $"{prefix}/commands/{commandId}";
        return TypedResults.Accepted(url, new CommandAccepted(commandId, url));
    }

    /// <summary>Map <c>GET commands/{id}</c> on a module's group, backed by its <typeparamref name="TContext"/>.</summary>
    public static RouteGroupBuilder MapCommandStatus<TContext>(this RouteGroupBuilder group) where TContext : DbContext =>
        group.MapCommandStatusInternal<TContext>();

    private static RouteGroupBuilder MapCommandStatusInternal<TContext>(this RouteGroupBuilder group) where TContext : DbContext
    {
        group.MapGet("/commands/{id:guid}", async Task<Results<Ok<CommandStatusResponse>, NotFound>> (
                Guid id, TContext db, CancellationToken ct) =>
            {
                var command = await db.Set<Command>().FindAsync([id], ct);
                return command is null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(new CommandStatusResponse(
                        command.Id, command.Status.ToString(), command.ErrorCode, command.ResultLocation));
            })
            .WithName($"{typeof(TContext).Name}CommandStatus")
            .WithSummary("Poll an async write's outcome.");
        return group;
    }
}

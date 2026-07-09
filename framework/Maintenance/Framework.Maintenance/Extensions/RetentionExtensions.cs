using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Maintenance;

public static class RetentionExtensions
{
    /// <summary>Register the retention sweeper hosted service. Safe to call more than once per host
    /// (the hosted service is de-duplicated by type). Register the sweeps it runs with
    /// <see cref="AddRetentionSweep{TSweep}"/>, and bind <see cref="RetentionOptions"/> separately.</summary>
    public static IServiceCollection AddRetentionSweeper(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<RetentionSweepService>();
        return services;
    }

    /// <summary>Register one <see cref="IRetentionSweep"/>. Scoped, so it can inject a DbContext.</summary>
    public static IServiceCollection AddRetentionSweep<TSweep>(this IServiceCollection services)
        where TSweep : class, IRetentionSweep
    {
        services.AddScoped<IRetentionSweep, TSweep>();
        return services;
    }
}

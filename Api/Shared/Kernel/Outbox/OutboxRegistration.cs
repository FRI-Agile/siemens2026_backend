using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kernel.Outbox;

/// <summary>Wires the transactional outbox: capture in producing modules, the dispatcher, and consumers' handlers.</summary>
public static class OutboxRegistration
{
    /// <summary>
    /// Registers the dispatcher and what producers and consumers share: the interceptor, the wake signal and the clock.
    /// The host calls it once; the host also registers the <see cref="ICurrentActor"/> the interceptor stamps rows with.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddOutbox(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IOutboxSignal, OutboxSignal>();
        services.TryAddSingleton<ChangeCaptureInterceptor>();
        services.AddHostedService<OutboxDispatcher>();
        return services;
    }

    /// <summary>
    /// Makes a module context capture its <see cref="ITrackedEntity"/> changes on save; call from its options callback.
    /// <see cref="AddOutboxProducer{TContext}"/> then lets the dispatcher drain them.
    /// </summary>
    /// <param name="options">The module context's options builder.</param>
    /// <param name="services">The provider the options callback receives.</param>
    /// <returns>The same <paramref name="options"/> for chaining.</returns>
    public static DbContextOptionsBuilder AddChangeCapture(this DbContextOptionsBuilder options, IServiceProvider services) =>
        options.AddInterceptors(services.GetRequiredService<ChangeCaptureInterceptor>());

    /// <summary>Lets the dispatcher drain <typeparamref name="TContext"/>'s outbox.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddOutboxProducer<TContext>(this IServiceCollection services)
        where TContext : DbContext, IOutboxDbContext
    {
        services.AddScoped<IOutboxDbContext>(sp => sp.GetRequiredService<TContext>());
        return services;
    }

    /// <summary>Registers every <see cref="IEntityChangeHandler"/> in <paramref name="assembly"/> (non-public ones too), scoped.</summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="assembly">The consuming module's assembly.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddEntityChangeHandlers(this IServiceCollection services, Assembly assembly)
    {
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(c => c.AssignableTo<IEntityChangeHandler>(), publicOnly: false)
                .As<IEntityChangeHandler>().WithScopedLifetime());
        return services;
    }
}

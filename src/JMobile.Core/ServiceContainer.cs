namespace JMobile.Core;

/// <summary>
/// The small service registry the virtual machine exposes to its runtime library.
/// </summary>
/// <remarks>
/// The core deliberately knows nothing about displays, audio devices or record stores. The
/// runtime library asks for a contract it cares about, and the emulator host registers an
/// implementation at startup, so the dependency points from the concrete host towards the
/// abstraction rather than the other way round.
/// <para>
/// This is a service locator, which is normally a design smell because it hides dependencies.
/// It earns its place here for one reason: the consumers are native method bodies, which are
/// delegates called by the interpreter and therefore have no constructor to inject into.
/// Everywhere else in this codebase dependencies are passed explicitly.
/// </para>
/// </remarks>
public sealed class ServiceContainer
{
    private readonly Dictionary<Type, object> _services = [];
    private readonly Lock _gate = new();

    /// <summary>Registers an implementation, replacing any previous registration.</summary>
    public void Register<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        lock (_gate)
        {
            _services[typeof(TService)] = instance;
        }
    }

    /// <summary>Resolves a service, or returns null when the host did not provide one.</summary>
    public TService? Get<TService>() where TService : class
    {
        lock (_gate)
        {
            return _services.TryGetValue(typeof(TService), out object? service) ? (TService)service : null;
        }
    }

    /// <summary>Resolves a service the runtime cannot work without.</summary>
    public TService Require<TService>() where TService : class =>
        Get<TService>() ?? throw new InvalidOperationException(
            $"No implementation of {typeof(TService).Name} was registered with the virtual machine.");

    public bool Contains<TService>() where TService : class
    {
        lock (_gate)
        {
            return _services.ContainsKey(typeof(TService));
        }
    }
}

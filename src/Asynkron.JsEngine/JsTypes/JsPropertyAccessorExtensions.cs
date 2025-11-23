using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
/// Helper extensions to simplify attaching host functions to property accessors.
/// </summary>
public static class JsPropertyAccessorExtensions
{
    extension(IJsPropertyAccessor accessor)
    {
        /// <summary>
        ///     Convenience overload for host-backed functions.
        /// </summary>
        public void SetHostProperty(string name, Func<object?, IReadOnlyList<object?>, object?> handler)
        {
            accessor.SetProperty(name, new HostFunction(handler));
        }

        /// <summary>
        ///     Convenience overload for realm-aware host-backed functions.
        /// </summary>
        public void SetHostProperty(string name, Func<object?, IReadOnlyList<object?>, RealmState?, object?> handler,
            RealmState? realmState)
        {
            accessor.SetProperty(name, new HostFunction(handler, realmState));
        }

        /// <summary>
        ///     Convenience overload for realm-aware host-backed functions.
        /// </summary>
        public void SetHostedProperty(string name,
            Func<object?, IReadOnlyList<object?>, object?> handler)
        {
            accessor.SetHostProperty(name, handler);
        }
    }
}

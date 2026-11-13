using CK.AppIdentity;
using CK.Core;

namespace CK.TypeScript;

/// <summary>
/// The base class of the options of a <see cref="TypeScriptClient"/>: configured by their key in the client section
/// ("Local:PartyClient:{Name}:{Option}", the option name is the type name without its "Option" suffix).
/// <para>
/// A concrete option is decorated with <see cref="PartyClientOptionAttribute"/> and has one public constructor whose
/// parameters are resolved from the services of the host. An instance is created for each client application that enables
/// it, and <see cref="Initialize"/> is called when the client is created (a configuration error makes the client an error).
/// </para>
/// </summary>
public abstract class TypeScriptClientOption : IPartyComponentOption<TypeScriptClient>
{
    /// <summary>
    /// Reads and checks the configuration of this option for a client application. Does nothing by default.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="client">The client application.</param>
    /// <param name="configuration">
    /// The section of this option in the client section. Null when the option is enabled by default or by <c>true</c>.
    /// </param>
    /// <returns>True on success, false on error (errors must be logged).</returns>
    public virtual bool Initialize( IActivityMonitor monitor, TypeScriptClient client, ImmutableConfigurationSection? configuration ) => true;
}

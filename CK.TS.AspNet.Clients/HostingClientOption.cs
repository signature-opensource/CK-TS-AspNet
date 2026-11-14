using CK.AppIdentity;
using CK.Core;

namespace CK.TypeScript;

/// <summary>
/// The base class of the options of the <see cref="HostingClientFeature"/>: configured by their key in the "Hosting"
/// section of a client ("Local:PartyClient:{Name}:Hosting:{Option}", the option name is the type name without its
/// "Option" suffix).
/// <para>
/// A concrete option is decorated with <see cref="PartyClientOptionAttribute"/> and has one public constructor whose
/// parameters are resolved from the services of the host. An instance is created for each client that enables it, and
/// <see cref="Initialize"/> is called when the feature is created (a configuration error makes the client an error).
/// </para>
/// </summary>
public abstract class HostingClientOption : IPartyComponentOption<HostingClientFeature>
{
    /// <summary>
    /// Reads and checks the configuration of this option for a client. Does nothing by default.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="hosting">The hosting of the client.</param>
    /// <param name="configuration">
    /// The section of this option in the "Hosting" section. Null when the option is enabled by default or by <c>true</c>.
    /// </param>
    /// <returns>True on success, false on error (errors must be logged).</returns>
    public virtual bool Initialize( IActivityMonitor monitor, HostingClientFeature hosting, ImmutableConfigurationSection? configuration ) => true;
}
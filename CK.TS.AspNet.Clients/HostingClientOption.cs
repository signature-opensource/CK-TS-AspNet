using CK.AppIdentity;

namespace CK.TypeScript;

/// <summary>
/// The base class of the options of the <see cref="HostingClientFeature"/>: configured by their key in the "Hosting"
/// section of a client ("Local:PartyClient:{Name}:Hosting:{Option}", the option name is the type name without its
/// "Option" suffix).
/// <para>
/// A concrete option is decorated with <see cref="PartyClientOptionAttribute"/> and has one public constructor: its
/// <c>IConfiguration</c> parameter is its section (null when it is enabled by default or by <c>true</c>), read into its
/// properties (an invalid value throws and makes the client an error), the other ones are resolved from the services of the
/// host. An instance is created for each client that enables it, with its "Hosting" feature.
/// </para>
/// </summary>
public abstract class HostingClientOption : IPartyComponentOption<HostingClientFeature>
{
}

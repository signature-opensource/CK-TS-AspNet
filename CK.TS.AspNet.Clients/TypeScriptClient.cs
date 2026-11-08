using CK.AppIdentity;
using CK.Core;

namespace CK.TypeScript;

/// <summary>
/// A client application of a party: a TypeScript configuration of the BinPath that is a client application
/// (its <see cref="ClientInfo"/>), declared by the party in its "Local:PartyClient:{Name}" section. It is served by the
/// endpoints of its party (it requires the "HttpEndpoint" feature) and announced in its description.
/// </summary>
public sealed class TypeScriptClient : PartyClient
{
    internal TypeScriptClient( ILocalParty party, ClientInfo info, ImmutableConfigurationSection configuration )
        : base( party, info.Name, configuration )
    {
        Info = info;
    }

    /// <summary>
    /// Gets what the build says about this client application.
    /// </summary>
    public ClientInfo Info { get; }

    /// <summary>
    /// Gets the description of this client application: its name and its request path.
    /// </summary>
    public override ClientApplicationDescription Description => new ClientApplicationDescription( Name, Info.RequestPath );
}

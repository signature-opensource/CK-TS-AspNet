using CK.AppIdentity;
using CK.Core;
using System.Collections.Generic;
using System.Linq;

namespace CK.TypeScript;

/// <summary>
/// Creates the <see cref="TypeScriptClient"/> of the parties that declare them: the client applications of the
/// <see cref="ClientDirectory"/>. They require the "HttpEndpoint" feature of their party.
/// </summary>
public sealed class TypeScriptClientFactory : IPartyClientFactory
{
    readonly ClientDirectory _directory;

    /// <summary>
    /// Initializes a new factory.
    /// </summary>
    /// <param name="directory">The client applications of the BinPath.</param>
    /// <param name="httpEndpoint">The "HttpEndpoint" feature that serves them.</param>
    public TypeScriptClientFactory( ClientDirectory directory, HttpEndpointFeatureDriver httpEndpoint )
    {
        _directory = directory;
        ClientNames = directory.Clients.Select( c => c.Name ).ToArray();
        RequiredFeatures = [httpEndpoint];
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> ClientNames { get; }

    /// <inheritdoc />
    public IReadOnlyList<ApplicationIdentityFeatureDriver> RequiredFeatures { get; }

    /// <inheritdoc />
    public PartyClient? Create( IActivityMonitor monitor, ILocalParty party, string name, ImmutableConfigurationSection configuration )
    {
        // Everything a client application is comes from the build: its configuration has no fact.
        var facts = configuration.GetChildren();
        if( facts.Count > 0 )
        {
            monitor.Error( $"The client application '{name}' of '{party.FullName}' has no configurable fact: '{facts.Select( f => f.Key ).Concatenate( "', '" )}' is not expected in \"Local:Client:{name}\"." );
            return null;
        }
        return new TypeScriptClient( party, _directory.Clients.Single( c => c.Name == name ), configuration );
    }
}

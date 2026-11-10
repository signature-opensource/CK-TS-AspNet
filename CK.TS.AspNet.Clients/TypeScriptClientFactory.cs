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
    readonly PartyComponentOptionDirectory _options;

    /// <summary>
    /// Initializes a new factory.
    /// </summary>
    /// <param name="directory">The client applications of the BinPath.</param>
    /// <param name="httpEndpoint">The "HttpEndpoint" feature that serves them.</param>
    /// <param name="options">The options of the party components: the option keys of a client section are not its facts.</param>
    public TypeScriptClientFactory( ClientDirectory directory, HttpEndpointFeatureDriver httpEndpoint, PartyComponentOptionDirectory options )
    {
        _directory = directory;
        _options = options;
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
        // Everything a client application is comes from the build: its configuration has no fact, only options (selected
        // by the PartyClient feature).
        var facts = configuration.GetChildren().Where( c => !_options.IsOptionName( typeof( TypeScriptClient ), c.Key ) ).ToList();
        if( facts.Count > 0 )
        {
            monitor.Error( $"The client application '{name}' of '{party.FullName}' has no configurable fact: '{facts.Select( f => f.Key ).Concatenate( "', '" )}' is not expected in \"Local:PartyClient:{name}\" (only its options are)." );
            return null;
        }
        return new TypeScriptClient( party, _directory.Clients.Single( c => c.Name == name ), configuration );
    }
}

using CK.AppIdentity;
using CK.Core;
using System.Collections.Generic;
using System.Linq;

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
    /// Gets the description of this client application: its name, its request path and its facts (see <see cref="ClientInfo.Facts"/>).
    /// </summary>
    public override ClientApplicationDescription Description => new ClientApplicationDescription( Name, Info.RequestPath, Info.Facts );

    /// <summary>
    /// Gets how the built files of this client application are served. Null when its "StaticFiles" option is disabled
    /// (<c>"StaticFiles": false</c>): the files are served without cache and security headers.
    /// </summary>
    public StaticFilesOption? StaticFiles => Options.OfType<StaticFilesOption>().SingleOrDefault();

    /// <summary>
    /// Initializes the options of this client application: they are <see cref="TypeScriptClientOption"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="options">The selected options and their configuration.</param>
    /// <returns>True on success, false on error.</returns>
    protected override bool InitializeOptions( IActivityMonitor monitor, IReadOnlyList<(object Option, ImmutableConfigurationSection? Configuration)> options )
    {
        bool success = true;
        foreach( var (option, configuration) in options )
        {
            if( option is TypeScriptClientOption o )
            {
                success &= o.Initialize( monitor, this, configuration );
            }
            else
            {
                monitor.Error( $"The option '{option.GetType().Name}' of {this} must be a {nameof( TypeScriptClientOption )}." );
                success = false;
            }
        }
        return success;
    }
}

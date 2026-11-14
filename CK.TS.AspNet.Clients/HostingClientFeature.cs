using CK.AppIdentity;
using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.TypeScript;

/// <summary>
/// The "Hosting" client feature: the client is served by its party, under its <see cref="RequestPath"/>, by the "HttpEndpoint"
/// feature of the party (see <see cref="ClientsApplicationExtensions.MapClients"/>). It requires the web build of the
/// client (<see cref="IWebAppBuild"/>: "NgApp" for an Angular application) for its built files and its dev server.
/// <para>
/// Its "RequestPath" and "DevServerPort" are compile time keys (the build writes them in the client's project): they come
/// from the build (<see cref="Info"/>). Its run time keys are "UseDist" and its options (<see cref="HostingClientOption"/>:
/// "StaticFiles").
/// </para>
/// </summary>
[Requires<HttpEndpointFeatureDriver, IWebAppBuild>]
public sealed class HostingClientFeature : ClientFeature
{
    internal HostingClientFeature( PartyClient client, ImmutableConfigurationSection configuration, ClientInfo info, IWebAppBuild build, bool useDist )
        : base( client, configuration )
    {
        Info = info;
        Build = build;
        UseDist = useDist;
    }

    /// <summary>
    /// Gets what the build says about the client application (its request path, its dev server port).
    /// </summary>
    public ClientInfo Info { get; }

    /// <summary>
    /// Gets the web build of the client: its project, its output and its dev server.
    /// </summary>
    public IWebAppBuild Build { get; }

    /// <summary>
    /// Gets the request path under which the party serves the client ("/" for its default one).
    /// </summary>
    public string RequestPath => Info.RequestPath;

    /// <summary>
    /// Gets whether the party serves the built files of the client even in Development (its "UseDist", false by default).
    /// The host's configuration overrides it for a run: "Clients:{Name}:UseDist", "Clients:UseDist".
    /// </summary>
    public bool UseDist { get; }

    /// <summary>
    /// Gets how the built files are served. Null when its "StaticFiles" option is disabled (<c>"StaticFiles": false</c>):
    /// the files are served without cache and security headers.
    /// </summary>
    public StaticFilesOption? StaticFiles => Options.OfType<StaticFilesOption>().SingleOrDefault();

    /// <summary>
    /// Initializes the options of the hosting: they are <see cref="HostingClientOption"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="options">The selected options and their configuration.</param>
    /// <returns>True on success, false on error.</returns>
    protected override bool InitializeOptions( IActivityMonitor monitor, IReadOnlyList<(object Option, ImmutableConfigurationSection? Configuration)> options )
    {
        bool success = true;
        foreach( var (option, configuration) in options )
        {
            if( option is HostingClientOption o )
            {
                success &= o.Initialize( monitor, this, configuration );
            }
            else
            {
                monitor.Error( $"The option '{option.GetType().Name}' of {this} must be a {nameof( HostingClientOption )}." );
                success = false;
            }
        }
        return success;
    }

    /// <summary>
    /// Contributes the request path of the client to its description.
    /// </summary>
    /// <param name="builder">The description of the client.</param>
    protected override void ContributeDescription( ClientDescriptionBuilder builder ) => builder.RequestPath = RequestPath;
}

/// <summary>
/// Creates the "Hosting" feature of the clients that declare it: the client applications of the <see cref="ClientDirectory"/>.
/// </summary>
public sealed class HostingClientFeatureDriver : ClientFeatureDriver<HostingClientFeature>
{
    static readonly HashSet<string> _compileKeys = new( StringComparer.OrdinalIgnoreCase ) { "RequestPath", "DevServerPort" };

    readonly ClientDirectory _directory;
    readonly PartyComponentOptionDirectory _options;

    /// <summary>
    /// Initializes a new driver.
    /// </summary>
    /// <param name="directory">The client applications built by the application.</param>
    /// <param name="options">The options of the party components: the option keys of the section are not its own.</param>
    public HostingClientFeatureDriver( ClientDirectory directory, PartyComponentOptionDirectory options )
    {
        _directory = directory;
        _options = options;
    }

    /// <inheritdoc />
    protected override HostingClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration )
    {
        var info = _directory.Clients.FirstOrDefault( c => c.Name == client.Name );
        if( info == null )
        {
            monitor.Error( $"The {client} declares \"Hosting\" but the application builds no client application '{client.Name}'." );
            return null;
        }
        // The requirements are checked: the web build is here.
        var build = client.GetFeature<IWebAppBuild>();
        Throw.CheckState( build != null );
        bool success = true;
        bool useDist = false;
        foreach( var k in configuration.GetChildren() )
        {
            if( k.Key.Equals( "UseDist", StringComparison.OrdinalIgnoreCase ) )
            {
                if( !bool.TryParse( k.Value, out useDist ) )
                {
                    monitor.Error( $"Invalid \"{k.Path}\": '{k.Value}' (true or false is expected)." );
                    success = false;
                }
            }
            // The compile time keys are here only when the App keeps them ("ProjectCompileKeys").
            else if( !_compileKeys.Contains( k.Key ) && !_options.IsOptionName( typeof( HostingClientFeature ), k.Key ) )
            {
                monitor.Error( $"Unknown \"{k.Path}\": the keys of \"Hosting\" are UseDist, its options and its compile time keys (RequestPath, DevServerPort)." );
                success = false;
            }
        }
        return success ? new HostingClientFeature( client, configuration, info, build, useDist ) : null;
    }
}
using CK.AppIdentity;
using CK.Core;
using System.Linq;

namespace CK.TypeScript;

/// <summary>
/// The "Hosting" client feature: the client is served by its party, under its <see cref="RequestPath"/>, by the "HttpEndpoint"
/// feature of the party (see <see cref="ClientsApplicationExtensions.MapClients"/>). It requires the web build of the
/// client (<see cref="IWebAppBuild"/>: "NgApp" for an Angular application) for its built files and its dev server.
/// <para>
/// Its keys are its model (<see cref="HostingConfiguration"/>: "UseDist") and its options (<see cref="HostingClientOption"/>:
/// "StaticFiles"). The request path and the dev server port of the client are the web build's (the compile time model of
/// "NgApp"): they come from the build (<see cref="Info"/>).
/// </para>
/// </summary>
[Requires<HttpEndpointFeatureDriver, IWebAppBuild>]
[PartyClientFeature( "Hosting", TypeActivation.Optional )]
[Configuration<HostingConfiguration>]
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
    public StaticFilesOption? StaticFiles => GetOption<StaticFilesOption>();

    /// <summary>
    /// Contributes the request path of the client to its description.
    /// </summary>
    /// <param name="builder">The description of the client.</param>
    protected override void ContributeDescription( ClientDescriptionBuilder builder ) => builder.RequestPath = RequestPath;
}

/// <summary>
/// The configuration model of the "Hosting" client feature (its options are next to its keys).
/// </summary>
[ConfigurationModel]
public sealed class HostingConfiguration : IConfigurationModel
{
    /// <summary>
    /// Gets whether the party serves the built files of the client even in Development (false by default). The host's
    /// configuration overrides it for a run: "Clients:{Name}:UseDist", "Clients:UseDist".
    /// </summary>
    public bool UseDist { get; init; }
}

/// <summary>
/// Creates the "Hosting" feature of the clients that declare it: the client applications of the <see cref="ClientDirectory"/>.
/// </summary>
public sealed class HostingClientFeatureDriver : ClientFeatureDriver<HostingClientFeature, HostingConfiguration>
{
    readonly ClientDirectory _directory;

    /// <summary>
    /// Initializes a new driver.
    /// </summary>
    /// <param name="directory">The client applications built by the application.</param>
    public HostingClientFeatureDriver( ClientDirectory directory )
    {
        _directory = directory;
    }

    /// <inheritdoc />
    protected override HostingClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration, HostingConfiguration model )
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
        return new HostingClientFeature( client, configuration, info, build, model.UseDist );
    }
}
using CK.AppIdentity;
using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Linq;

namespace CK.TypeScript;

/// <summary>
/// Serves the clients of the root party that have the "Hosting" feature (<see cref="HostingClientFeature"/>).
/// </summary>
public static class ClientsApplicationExtensions
{
    /// <summary>
    /// Serves each client of the root party that has the "Hosting" feature under its <see cref="HostingClientFeature.RequestPath"/>
    /// (the default one, served at "/", is mapped last):
    /// <list type="bullet">
    ///   <item>
    ///   In Development, by its dev server: the host starts it (its web build's <see cref="IWebAppBuild.GetDevServerStart"/> on its <see cref="ClientInfo.DevServerPort"/>, stopped
    ///   with the host) unless something already listens on this port, and forwards to it what the host doesn't handle under the
    ///   request path: the host stays the origin. A dev server that exits while the host runs is an error (its exit code and its
    ///   last output lines), that the running status reports (<see cref="ClientsDevRunningStatusContributor"/>).
    ///   </item>
    ///   <item>
    ///   Otherwise, or when its "dist" is used (<see cref="HostingClientFeature.UseDist"/>, that the host's configuration overrides with
    ///   "Clients:{Name}:UseDist" or "Clients:UseDist"), by its built files (<see cref="IWebAppBuild.ProjectPath"/>/<see cref="IWebAppBuild.OutputPath"/>,
    ///   relative to the content root) with a fallback to its "index.html" for its client routes, the caching and the security
    ///   headers of its <see cref="HostingClientFeature.StaticFiles"/> option. A client application that is not built is skipped
    ///   with a warning.
    ///   </item>
    /// </list>
    /// </summary>
    /// <param name="app">This application.</param>
    /// <returns>This application.</returns>
    public static WebApplication MapClients( this WebApplication app )
    {
        MapClientsCore( app );
        return app;
    }

    /// <summary>
    /// Serves the client applications (see <see cref="MapClients"/>) in one gesture on the builder (the "Enable" convention
    /// of CK.AspNet: this is idempotent). <see cref="ClientsHosting"/> contributes it to the web applications of the host.
    /// </summary>
    /// <param name="builder">This builder.</param>
    /// <returns>This builder.</returns>
    public static WebApplicationBuilder EnableClients( this WebApplicationBuilder builder )
    {
        if( builder.TryEnable( "Clients" ) )
        {
            // CKBuild gives the WebApplication.
            builder.AppendApplicationBuilder( app => MapClientsCore( (WebApplication)app ) );
        }
        return builder;
    }

    static void MapClientsCore( WebApplication app )
    {
        var monitor = ActivityMonitor.StaticLogger;
        var isDevelopment = app.Environment.IsDevelopment();
        // The running status of a development party (when the DevRunningStatus feature of CK.AppIdentity is here).
        var status = app.Services.GetService<ClientsDevRunningStatusContributor>();
        // The clients of the root party that it serves ("Local:PartyClient:{Name}:Hosting", the "PartyClient" feature),
        // the default client application ("/") last.
        var hosted = app.Services.GetService<IApplicationIdentityService>()?.GetFeature<PartyClientFeature>()?.Clients
                                 .Select( c => c.GetFeature<HostingClientFeature>() )
                                 .OfType<HostingClientFeature>()
                                 .OrderBy( h => h.RequestPath == "/" )
                                 .ToList()
                     ?? [];
        foreach( var hosting in hosted )
        {
            var client = hosting.Info;
            // The default client application ("/") has an empty path prefix.
            var requestPath = hosting.RequestPath.TrimEnd( '/' );
            var projectPath = Path.Combine( app.Environment.ContentRootPath, hosting.Build.ProjectPath );
            if( isDevelopment && !UseDist( monitor, app.Configuration, hosting ) )
            {
                // Mapped first: the dev server can fail at once.
                status?.OnMapped( client, useDist: false );
                DevServer.EnsureRunning( monitor, app, client, hosting.Build, projectPath, status );
                app.MapForwarder( $"{requestPath}/{{**catch-all}}", $"http://localhost:{client.DevServerPort}" )
                   .Add( b => ((RouteEndpointBuilder)b).Order = int.MaxValue );
            }
            else if( MapDist( monitor, app, client, hosting.StaticFiles, requestPath, Path.Combine( projectPath, hosting.Build.OutputPath ) ) )
            {
                status?.OnMapped( client, useDist: true );
            }
        }
    }

    static bool UseDist( IStaticLogger monitor, IConfiguration configuration, HostingClientFeature hosting )
    {
        var client = hosting.Info;
        var key = $"Clients:{client.Name}:UseDist";
        var value = configuration[key];
        if( value == null )
        {
            key = "Clients:UseDist";
            value = configuration[key];
        }
        if( value == null ) return hosting.UseDist;
        if( bool.TryParse( value, out var useDist ) ) return useDist;
        monitor.Warn( $"Invalid '{key}' configuration '{value}' (expecting true or false): client application '{client}' uses its default (UseDist = {hosting.UseDist})." );
        return hosting.UseDist;
    }

    static bool MapDist( IStaticLogger monitor, WebApplication app, ClientInfo client, StaticFilesOption? staticFiles, string requestPath, string output )
    {
        if( !Directory.Exists( output ) )
        {
            monitor.Warn( $"Client application '{client}' is not built ('{output}' not found): it is not served." );
            return false;
        }
        monitor.Info( $"Client application '{client}' is served from '{output}'." );
        var files = new PhysicalFileProvider( output );
        // Without its "StaticFiles" option (disabled), the files are served without cache and security headers.
        var staticFileOptions = staticFiles?.CreateStaticFileOptions( files, requestPath )
                                ?? new StaticFileOptions { FileProvider = files, RequestPath = requestPath };
        app.UseDefaultFiles( staticFiles?.CreateDefaultFilesOptions( files, requestPath )
                             ?? new DefaultFilesOptions { FileProvider = files, RequestPath = requestPath } );
        app.UseStaticFiles( staticFileOptions );
        // The fallback rewrites the request path to the file path: it must be under the RequestPath.
        app.MapFallbackToFile( $"{requestPath}/{{*path:nonfile}}",
                               $"{requestPath.TrimStart( '/' )}/index.html".TrimStart( '/' ),
                               staticFileOptions );
        return true;
    }
}

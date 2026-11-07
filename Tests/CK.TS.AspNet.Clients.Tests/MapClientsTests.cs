using CK.AspNet;
using CK.Core;
using CK.Setup;
using CK.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.TypeScript.Tests;

[TestFixture]
public class MapClientsTests
{
    static void CreateBuiltClient( NormalizedPath projectPath, string name, string content )
    {
        var output = projectPath.Combine( $"dist/{name}/browser" );
        Directory.CreateDirectory( output );
        File.WriteAllText( output.AppendPart( "index.html" ), content );
        File.WriteAllText( output.AppendPart( "main.js" ), $"// {name}" );
    }

    // Runs the engine with the App (default client application) and the MiniApp, both built ("dist").
    static async Task<IStObjMap> CreateMapAsync( NormalizedPath root, int appPort, int miniAppPort, bool miniAppUseDist = false )
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        // A party: the "HttpEndpoint" feature serves the client applications on its endpoint (the contribution of ClientsHosting).
        configuration.FirstBinPath.Assemblies.Add( AspNetServerTestHelperExtensions.HttpEndpointAssembly );
        configuration.FirstBinPath.Types.Add( typeof( ClientDirectory ),
                                              typeof( ClientsHosting ),
                                              typeof( ClientsDevRunningStatusContributor ),
                                              typeof( ClientsPartyDescriptionContributor ) );
        configuration.EnsureAspect<TypeScriptAspectConfiguration>();
        var app = configuration.FirstBinPath.EnsureAspect<TypeScriptBinPathAspectConfiguration>();
        app.Name = "App";
        app.Kind = ClientKind.DefaultApp;
        app.DevServerPort = appPort;
        app.TargetProjectPath = root.AppendPart( "App" );
        app.IntegrationMode = CKGenIntegrationMode.None;
        app.AddOtherConfiguration( new TypeScriptBinPathAspectConfiguration
        {
            Name = "MiniApp",
            Kind = ClientKind.App,
            DevServerPort = miniAppPort,
            UseDist = miniAppUseDist,
            TargetProjectPath = root.AppendPart( "MiniApp" ),
            IntegrationMode = CKGenIntegrationMode.None
        } );
        var map = (await configuration.RunSuccessfullyAsync()).LoadMap();
        CreateBuiltClient( root.AppendPart( "App" ), "App", "THE-APP" );
        CreateBuiltClient( root.AppendPart( "MiniApp" ), "MiniApp", "THE-MINI-APP" );
        return map;
    }

    // A fake dev server ("ng serve") that answers with its name and the path it receives.
    static async Task<WebApplication> StartDevServerAsync( string name )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls( "http://127.0.0.1:0" );
        var devServer = builder.Build();
        devServer.MapGet( "/{**path}", ( Microsoft.AspNetCore.Http.HttpContext ctx ) => $"{name}:{ctx.Request.Path}" );
        await devServer.StartAsync();
        return devServer;
    }

    static int Port( WebApplication app ) => new Uri( app.Urls.First() ).Port;

    // A party: the contribution of ClientsHosting serves the client applications on its endpoint. The client projects are
    // under the content root (the test project folder): their ProjectPath is relative.
    static Task<RunningAspNetServer> CreateHostAsync( IStObjMap map, string environment, params (string Key, string Value)[] configuration )
    {
        return map.CreateRunningAspNetServerAsync( configureHost: b =>
        {
            b.Environment.EnvironmentName = environment;
            b.Configuration.AddInMemoryCollection( configuration.Select( c => new KeyValuePair<string, string?>( c.Key, c.Value ) ) );
        } );
    }

    static async Task<string> GetAsync( RunningAspNetServer server, string path ) => await (await server.Client.GetAsync( path )).Content.ReadAsStringAsync();

    [Test]
    public async Task in_Production_the_built_client_applications_are_served_Async()
    {
        var map = await CreateMapAsync( TestHelper.CleanupFolder( TestHelper.GetTypeScriptGeneratedOnlyTargetProjectPath(), false ), 4200, 4201 );
        await using var server = await CreateHostAsync( map, "Production" );

        (await GetAsync( server, "/" )).ShouldBe( "THE-APP" );
        (await GetAsync( server, "/main.js" )).ShouldBe( "// App" );
        // A client route of the default application.
        (await GetAsync( server, "/some/route" )).ShouldBe( "THE-APP" );

        (await GetAsync( server, "/mini-app/" )).ShouldBe( "THE-MINI-APP" );
        (await GetAsync( server, "/mini-app/main.js" )).ShouldBe( "// MiniApp" );
        // A client route of the MiniApp.
        (await GetAsync( server, "/mini-app/some/route" )).ShouldBe( "THE-MINI-APP" );
    }

    [Test]
    public async Task the_contribution_of_ClientsHosting_serves_the_client_applications_once_Async()
    {
        var map = await CreateMapAsync( TestHelper.CleanupFolder( TestHelper.GetTypeScriptGeneratedOnlyTargetProjectPath(), false ), 4200, 4201 );
        // The map registers the contribution of ClientsHosting, the "HttpEndpoint" feature applies it. The application
        // enables it too: the client applications are mapped once (twice would be ambiguous routes).
        await using var server = await map.CreateRunningAspNetServerAsync( configureEndpoint: web => web.EnableClients() );

        (await GetAsync( server, "/some/route" )).ShouldBe( "THE-APP" );
        (await GetAsync( server, "/mini-app/some/route" )).ShouldBe( "THE-MINI-APP" );
    }

    [Test]
    public async Task in_Development_the_client_applications_are_served_by_their_dev_server_Async()
    {
        // The dev servers are already running: the host uses them (it doesn't start "ng serve").
        await using var appDev = await StartDevServerAsync( "APP-DEV" );
        await using var miniDev = await StartDevServerAsync( "MINI-DEV" );
        var map = await CreateMapAsync( TestHelper.CleanupFolder( TestHelper.GetTypeScriptGeneratedOnlyTargetProjectPath(), false ), Port( appDev ), Port( miniDev ) );
        await using var server = await CreateHostAsync( map, "Development" );

        // The host forwards what it doesn't handle under the request path: it stays the origin.
        (await GetAsync( server, "/some/route" )).ShouldBe( "APP-DEV:/some/route" );
        (await GetAsync( server, "/mini-app/some/route" )).ShouldBe( "MINI-DEV:/mini-app/some/route" );
    }

    [Test]
    public async Task in_Development_UseDist_serves_the_built_files_and_the_host_configuration_overrides_it_Async()
    {
        await using var appDev = await StartDevServerAsync( "APP-DEV" );
        await using var miniDev = await StartDevServerAsync( "MINI-DEV" );
        // The MiniApp is configured to use its dist.
        var map = await CreateMapAsync( TestHelper.CleanupFolder( TestHelper.GetTypeScriptGeneratedOnlyTargetProjectPath(), false ), Port( appDev ), Port( miniDev ), miniAppUseDist: true );
        await using( var server = await CreateHostAsync( map, "Development" ) )
        {
            (await GetAsync( server, "/mini-app/some/route" )).ShouldBe( "THE-MINI-APP" );
            (await GetAsync( server, "/some/route" )).ShouldBe( "APP-DEV:/some/route" );
        }
        // For a run: all the client applications use their dist...
        await using( var server = await CreateHostAsync( map, "Development", ("Clients:UseDist", "true") ) )
        {
            (await GetAsync( server, "/some/route" )).ShouldBe( "THE-APP" );
            (await GetAsync( server, "/mini-app/some/route" )).ShouldBe( "THE-MINI-APP" );
        }
        // ...or the MiniApp uses its dev server.
        await using( var server = await CreateHostAsync( map, "Development", ("Clients:MiniApp:UseDist", "false") ) )
        {
            (await GetAsync( server, "/mini-app/some/route" )).ShouldBe( "MINI-DEV:/mini-app/some/route" );
        }
    }

    // The "Clients" section of the running status, by client name.
    static Dictionary<string, JsonElement> GetStatus( RunningAspNetServer server )
    {
        var contributor = server.Services.GetRequiredService<ClientsDevRunningStatusContributor>();
        using var stream = new MemoryStream();
        using( var w = new Utf8JsonWriter( stream ) )
        {
            w.WriteStartObject();
            contributor.Write( w );
            w.WriteEndObject();
        }
        return JsonDocument.Parse( stream.ToArray() ).RootElement.GetProperty( "Clients" ).EnumerateArray()
                           .ToDictionary( c => c.GetProperty( "Name" ).GetString()!, c => c.Clone() );
    }

    [Test]
    public async Task in_Production_the_running_status_has_the_built_client_applications_serving_Async()
    {
        var map = await CreateMapAsync( TestHelper.CleanupFolder( TestHelper.GetTypeScriptGeneratedOnlyTargetProjectPath(), false ), 4200, 4201 );
        await using var server = await CreateHostAsync( map, "Production" );
        server.Services.GetRequiredService<ClientsDevRunningStatusContributor>().IsReady.ShouldBeTrue();
        var status = GetStatus( server );
        status.Count.ShouldBe( 2 );
        status["App"].GetProperty( "Kind" ).GetString().ShouldBe( "DefaultApp" );
        status["App"].GetProperty( "RequestPath" ).GetString().ShouldBe( "/" );
        status["App"].GetProperty( "DevServerPort" ).GetInt32().ShouldBe( 4200 );
        status["MiniApp"].GetProperty( "Kind" ).GetString().ShouldBe( "App" );
        status["MiniApp"].GetProperty( "RequestPath" ).GetString().ShouldBe( "/mini-app" );
        foreach( var c in status.Values )
        {
            c.GetProperty( "UseDist" ).GetBoolean().ShouldBeTrue();
            c.GetProperty( "Serving" ).GetBoolean().ShouldBeTrue();
        }
    }

    [Test]
    // The engine runs in the test: the parallel builds of the stack can make it slow.
    [CancelAfter( 60000 )]
    public async Task in_Development_a_client_application_is_serving_once_its_dev_server_answers_Async( CancellationToken cancel )
    {
        // The probe tries IPv4 and IPv6 at once: the failed attempt must be observed.
        Exception? unobserved = null;
        EventHandler<UnobservedTaskExceptionEventArgs> onUnobserved = ( _, e ) => unobserved = e.Exception;
        TaskScheduler.UnobservedTaskException += onUnobserved;
        await using var appDev = await StartDevServerAsync( "APP-DEV" );
        await using var miniDev = await StartDevServerAsync( "MINI-DEV" );
        var map = await CreateMapAsync( TestHelper.CleanupFolder( TestHelper.GetTypeScriptGeneratedOnlyTargetProjectPath(), false ), Port( appDev ), Port( miniDev ), miniAppUseDist: true );
        await using var server = await CreateHostAsync( map, "Development" );
        var status = GetStatus( server );
        status["App"].GetProperty( "UseDist" ).GetBoolean().ShouldBeFalse();
        status["App"].GetProperty( "Serving" ).GetBoolean().ShouldBeFalse( "Not probed until the status is written." );
        status["MiniApp"].GetProperty( "UseDist" ).GetBoolean().ShouldBeTrue();
        status["MiniApp"].GetProperty( "Serving" ).GetBoolean().ShouldBeTrue();

        // The DevRunningStatus feature activates the contributor: the dev server is probed, a change is signaled.
        var changed = new TaskCompletionSource();
        server.Services.GetRequiredService<ClientsDevRunningStatusContributor>().Activate( TestHelper.Monitor, () => changed.TrySetResult() );
        await changed.Task.WaitAsync( cancel );
        GetStatus( server )["App"].GetProperty( "Serving" ).GetBoolean().ShouldBeTrue();

        // The losing attempt (the dev server listens on 127.0.0.1 only) is refused after about 2 seconds on Windows.
        await Task.Delay( 2500, cancel );
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        TaskScheduler.UnobservedTaskException -= onUnobserved;
        unobserved.ShouldBeNull();
    }

    [Test]
    [CancelAfter( 60000 )]
    public async Task in_Development_a_dev_server_that_exits_is_the_Error_of_its_client_application_Async( CancellationToken cancel )
    {
        var root = TestHelper.CleanupFolder( TestHelper.GetTypeScriptGeneratedOnlyTargetProjectPath(), false );
        // Nothing listens on its port: the host starts its dev server.
        var listener = new System.Net.Sockets.TcpListener( System.Net.IPAddress.Loopback, 0 );
        listener.Start();
        var appPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var map = await CreateMapAsync( root, appPort, 4201, miniAppUseDist: true );
        // The yarn of the App fails at once, like a "package.json" whose workspace is not installed.
        var releases = root.Combine( "App/.yarn/releases" );
        Directory.CreateDirectory( releases );
        File.WriteAllText( releases.AppendPart( "yarn-fake.cjs" ), """
            console.log( "Starting." );
            console.error( "Usage Error: The project in App/package.json doesn't seem to have been installed" );
            process.exit( 3 );
            """ );
        await using var server = await CreateHostAsync( map, "Development" );
        var changed = new SemaphoreSlim( 0 );
        server.Services.GetRequiredService<ClientsDevRunningStatusContributor>().Activate( TestHelper.Monitor, () => changed.Release() );
        JsonElement app;
        while( (app = GetStatus( server )["App"]).GetProperty( "Error" ).ValueKind == JsonValueKind.Null )
        {
            await changed.WaitAsync( cancel );
        }
        app.GetProperty( "Serving" ).GetBoolean().ShouldBeFalse();
        var error = app.GetProperty( "Error" ).GetString().ShouldNotBeNull();
        error.ShouldContain( "exit code 3" );
        error.ShouldContain( "Starting." );
        error.ShouldContain( "doesn't seem to have been installed" );
        // The MiniApp uses its dist: it is serving.
        var miniApp = GetStatus( server )["MiniApp"];
        miniApp.GetProperty( "Serving" ).GetBoolean().ShouldBeTrue();
        miniApp.GetProperty( "Error" ).ValueKind.ShouldBe( JsonValueKind.Null );
    }

    [Test]
    public async Task the_client_applications_are_contributed_to_the_description_of_the_party_Async()
    {
        var map = await CreateMapAsync( TestHelper.CleanupFolder( TestHelper.GetTypeScriptGeneratedOnlyTargetProjectPath(), false ), 4200, 4201 );
        await using var server = await CreateHostAsync( map, "Production" );
        var builder = new CK.AppIdentity.PartyDescription.Builder { PublicUrl = new Uri( "https://acme.example.com" ) };
        server.Services.GetRequiredService<ClientsPartyDescriptionContributor>().Contribute( builder );
        var description = builder.Build();
        description.ClientApplications.Select( a => a.Name ).ShouldBe( ["App", "MiniApp"], ignoreOrder: true );
        // The links of the other parties derive from them.
        description.GetClientApplicationUrl().ShouldBe( "https://acme.example.com" );
        description.GetClientApplicationUrl( "MiniApp" ).ShouldBe( "https://acme.example.com/mini-app" );
    }
}

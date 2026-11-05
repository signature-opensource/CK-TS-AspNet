using CK.AppIdentity;
using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CK.TypeScript;

/// <summary>
/// Contributes the "Clients" section of the running status of a development party (the "DevRunningStatus" feature of
/// CK.AppIdentity): the client applications served by <see cref="ClientsApplicationExtensions.MapClients"/>, with their
/// "Name", "Kind" ("DefaultApp" or "App"), "RequestPath", "DevServerPort", "UseDist" (the effective one), "Serving" and
/// "Error".
/// <para>
/// A client application that uses its "dist" is serving. One that uses its dev server is serving once its port answers:
/// the dev server compiles before it listens. The port is probed every second while the status is written and a dev
/// server doesn't answer yet. A client application that is not built (and not served) doesn't appear.
/// </para>
/// <para>
/// "Error" is null while the client application compiles or serves. When its dev server exits while the host runs, it is
/// the exit code and the last output lines of the dev server, and "Serving" is false: it will not be served.
/// </para>
/// </summary>
public sealed class ClientsDevRunningStatusContributor : IDevRunningStatusContributor, IDisposable
{
    readonly object _lock;
    readonly List<Client> _clients;
    Action? _onChanged;
    Timer? _probe;

    sealed class Client
    {
        public required ClientInfo Info { get; init; }
        public required bool UseDist { get; init; }
        public bool Serving { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Initializes a new <see cref="ClientsDevRunningStatusContributor"/>.
    /// </summary>
    public ClientsDevRunningStatusContributor()
    {
        _lock = new object();
        _clients = new List<Client>();
    }

    // Called by MapClients for each client application it serves (once per web application: the first one wins).
    internal void OnMapped( ClientInfo client, bool useDist )
    {
        Action? onChanged;
        lock( _lock )
        {
            if( _clients.Any( c => c.Info.Name == client.Name ) ) return;
            _clients.Add( new Client { Info = client, UseDist = useDist, Serving = useDist } );
            onChanged = _onChanged;
            if( onChanged != null ) EnsureProbe();
        }
        onChanged?.Invoke();
    }

    // Called by the DevServer when the dev server of a mapped client application exits while the host runs.
    internal void OnFailed( ClientInfo client, string error )
    {
        Action? onChanged;
        lock( _lock )
        {
            var c = _clients.FirstOrDefault( c => c.Info.Name == client.Name );
            if( c == null ) return;
            c.Serving = false;
            c.Error = error;
            onChanged = _onChanged;
        }
        onChanged?.Invoke();
    }

    /// <inheritdoc />
    public void Activate( IActivityMonitor monitor, Action onChanged )
    {
        lock( _lock )
        {
            _onChanged = onChanged;
            EnsureProbe();
        }
    }

    /// <summary>
    /// Always true: the client applications are served after the party is ready ("Serving").
    /// </summary>
    public bool IsReady => true;

    /// <inheritdoc />
    public void Write( Utf8JsonWriter writer )
    {
        writer.WriteStartArray( "Clients" );
        lock( _lock )
        {
            foreach( var c in _clients )
            {
                writer.WriteStartObject();
                writer.WriteString( "Name", c.Info.Name );
                // The default client application is the one served at the root.
                writer.WriteString( "Kind", c.Info.RequestPath == "/" ? "DefaultApp" : "App" );
                writer.WriteString( "RequestPath", c.Info.RequestPath );
                writer.WriteNumber( "DevServerPort", c.Info.DevServerPort );
                writer.WriteBoolean( "UseDist", c.UseDist );
                writer.WriteBoolean( "Serving", c.Serving );
                if( c.Error != null ) writer.WriteString( "Error", c.Error );
                else writer.WriteNull( "Error" );
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();
    }

    // Called in the lock.
    void EnsureProbe()
    {
        if( _probe == null && _clients.Any( IsWaiting ) )
        {
            _probe = new Timer( Probe, null, TimeSpan.Zero, TimeSpan.FromSeconds( 1 ) );
        }
    }

    void Probe( object? _ )
    {
        Client[] waiting;
        lock( _lock )
        {
            waiting = _clients.Where( IsWaiting ).ToArray();
        }
        bool changed = false;
        foreach( var c in waiting )
        {
            if( IsListening( c.Info.DevServerPort ) )
            {
                lock( _lock )
                {
                    // It may have failed meanwhile (something else may listen on its port).
                    if( c.Error == null )
                    {
                        c.Serving = true;
                        changed = true;
                    }
                }
            }
        }
        Action? onChanged;
        lock( _lock )
        {
            onChanged = _onChanged;
            if( _probe != null && !_clients.Any( IsWaiting ) )
            {
                _probe.Dispose();
                _probe = null;
            }
        }
        if( changed ) onChanged?.Invoke();
    }

    // A client application that is not serving yet and has not failed: its dev server compiles. Called in the lock.
    static bool IsWaiting( Client c ) => !c.Serving && c.Error == null;

    // A dev server listens on "localhost": IPv4 or IPv6. Both are tried at once: on Windows, a refused connection takes
    // about 2 seconds (connecting to "localhost" would wait for "::1" before trying "127.0.0.1").
    internal static bool IsListening( int port )
    {
        var attempts = Task.WhenAll( TryConnectAsync( IPAddress.Loopback, port ), TryConnectAsync( IPAddress.IPv6Loopback, port ) );
        return attempts.GetAwaiter().GetResult().Any( connected => connected );
    }

    // Each attempt is awaited (its failure is observed) and canceled after 500 ms (no pending connection is left behind).
    static async Task<bool> TryConnectAsync( IPAddress address, int port )
    {
        using var timeout = new CancellationTokenSource( 500 );
        using var c = new TcpClient( address.AddressFamily );
        try
        {
            await c.ConnectAsync( address, port, timeout.Token ).ConfigureAwait( false );
            return true;
        }
        catch( Exception )
        {
            return false;
        }
    }

    /// <summary>
    /// Stops the probe of the dev servers.
    /// </summary>
    public void Dispose()
    {
        lock( _lock )
        {
            _probe?.Dispose();
            _probe = null;
            _onChanged = null;
        }
    }
}

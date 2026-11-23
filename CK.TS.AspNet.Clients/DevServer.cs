using CK.Core;
using Microsoft.AspNetCore.Builder;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace CK.TypeScript;

/// <summary>
/// Starts the dev server of a client application (its <see cref="IWebAppBuild.GetDevServerStart"/> through its yarn) and stops it with the host.
/// A dev server that exits while the host runs is an error: its exit code and its last output lines.
/// <para>
/// On Windows, the dev server is in a <see cref="KillOnCloseJob"/>: its processes (yarn starts "ng serve" through an
/// intermediate process that exits, so a process tree kill misses it) are killed when the host stops, even abruptly.
/// Elsewhere, its process tree is killed when the host stops.
/// </para>
/// </summary>
static class DevServer
{
    // The output lines kept for the error of a dev server that exits.
    const int LastLinesCount = 10;

    internal static void EnsureRunning( IStaticLogger monitor,
                                        WebApplication app,
                                        ClientInfo client,
                                        CK.AppIdentity.IWebAppBuild build,
                                        string projectPath,
                                        ClientsDevRunningStatusContributor? status )
    {
        var port = client.DevServerPort;
        if( ClientsDevRunningStatusContributor.IsListening( port ) )
        {
            monitor.Info( $"Something listens on port {port}: it is used as the dev server of the client application '{client}'." );
            return;
        }
        var yarn = FindYarn( projectPath );
        if( yarn == null )
        {
            monitor.Warn( $"No yarn found (.yarn/releases/yarn*.cjs in or above '{projectPath}'): the dev server of the client application '{client}' must be started on port {port}." );
            return;
        }
        var start = build.GetDevServerStart( yarn, port );
        start.WorkingDirectory = projectPath;
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.CreateNoWindow = true;
        var process = Process.Start( start );
        if( process == null )
        {
            monitor.Warn( $"Unable to start the dev server of the client application '{client}'." );
            return;
        }
        // On Windows, the dev server and everything it starts are in a job that is killed with the host (see KillOnCloseJob).
        KillOnCloseJob? job = null;
        if( OperatingSystem.IsWindows() )
        {
            try
            {
                job = KillOnCloseJob.Assign( process );
            }
            catch( Exception ex )
            {
                monitor.Warn( $"Unable to assign the dev server of the client application '{client}' to a job: its processes may outlive the host.", ex );
            }
        }
        var lastLines = new Queue<string>( LastLinesCount );
        bool stopping = false;
        void OnLine( string? line, bool isError )
        {
            if( string.IsNullOrWhiteSpace( line ) ) return;
            lock( lastLines )
            {
                if( lastLines.Count == LastLinesCount ) lastLines.Dequeue();
                lastLines.Enqueue( line );
            }
            if( isError ) monitor.Warn( $"[{client.Name}] {line}" );
            else monitor.Info( $"[{client.Name}] {line}" );
        }
        process.OutputDataReceived += ( _, e ) => OnLine( e.Data, false );
        process.ErrorDataReceived += ( _, e ) => OnLine( e.Data, true );
        process.Exited += ( _, _ ) =>
        {
            if( Volatile.Read( ref stopping ) ) return;
            try
            {
                // Waits for the end of the redirected output.
                process.WaitForExit();
                var exitCode = process.ExitCode;
                string[] lines;
                lock( lastLines ) lines = lastLines.ToArray();
                var error = $"The dev server of the client application '{client}' (port {port}) exited (exit code {exitCode}).";
                monitor.Error( error );
                foreach( var line in lines ) monitor.Error( $"[{client.Name}] {line}" );
                status?.OnFailed( client, lines.Length > 0 ? $"{error}{Environment.NewLine}{string.Join( Environment.NewLine, lines )}" : error );
            }
            catch( Exception ex )
            {
                monitor.Error( $"While handling the exit of the dev server of the client application '{client}'.", ex );
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        // After the reads are started: a process that already exited raises Exited now.
        process.EnableRaisingEvents = true;
        app.Lifetime.ApplicationStopping.Register( () =>
        {
            Volatile.Write( ref stopping, true );
            try
            {
                if( process.HasExited )
                {
                    monitor.Info( $"The dev server of the client application '{client}' (port {port}) had already exited." );
                }
                else
                {
                    // Closing the job kills all its processes, the orphaned ones included.
                    if( job != null ) job.Dispose();
                    else process.Kill( entireProcessTree: true );
                    if( process.WaitForExit( 5000 ) )
                    {
                        monitor.Info( $"Stopped the dev server of the client application '{client}' (port {port})." );
                    }
                    else
                    {
                        monitor.Warn( $"The dev server of the client application '{client}' (port {port}) didn't exit within 5 seconds after being killed." );
                    }
                }
            }
            catch( Exception ex )
            {
                monitor.Warn( $"While stopping the dev server of the client application '{client}'.", ex );
            }
            // An exited dev server may have left processes in its job.
            job?.Dispose();
            process.Dispose();
        } );
        monitor.Info( $"Started the dev server of the client application '{client}' on port {port} (it compiles before it answers)." );
    }

    // Same lookup as the TypeScript engine: the first ".yarn/releases/yarn*js" in or above the project folder.
    static string? FindYarn( string projectPath )
    {
        for( var d = new DirectoryInfo( projectPath ); d != null; d = d.Parent )
        {
            var releases = Path.Combine( d.FullName, ".yarn", "releases" );
            if( Directory.Exists( releases ) )
            {
                var yarn = Directory.GetFiles( releases )
                                    .FirstOrDefault( f => Path.GetFileName( f ).StartsWith( "yarn" ) && f.EndsWith( "js" ) );
                if( yarn != null ) return yarn;
            }
        }
        return null;
    }
}

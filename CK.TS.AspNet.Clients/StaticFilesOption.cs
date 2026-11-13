using CK.AppIdentity;
using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CK.TypeScript;

/// <summary>
/// How the built files ("dist") of a client application are served: the caching, the security headers and the
/// <see cref="StaticFileOptions"/> that make sense in configuration. Enabled by default: a client application is always
/// served with the defaults below, its "Local:PartyClient:{Name}:StaticFiles" section only changes them, and
/// <c>"StaticFiles": false</c> serves the files without these headers (when a proxy or a CDN in front sets them).
/// <para>
/// The caching, by kind of file:
/// <list type="bullet">
///   <item>the HTML files ("index.html", also served for the client routes): <c>Cache-Control: no-cache</c> (always
///   revalidated, the ETag makes it cheap);</item>
///   <item>the hashed files (the name matches <c>HashedFilePattern</c>, by default a '-' and 8 base32 characters before
///   the extension: the esbuild names of the Angular builder, "main-VMMCN5NP.js"): <c>public, max-age=31536000, immutable</c>;</item>
///   <item>the other files (the copied assets, that can change without changing name): <c>no-cache</c>, or
///   <c>public, max-age=...</c> with <c>AssetsMaxAge</c>.</item>
/// </list>
/// </para>
/// <para>
/// The security headers: <c>X-Content-Type-Options: nosniff</c> and <c>Referrer-Policy: strict-origin-when-cross-origin</c>
/// on every file, <c>Content-Security-Policy: frame-ancestors 'none'</c> on the HTML files (only that directive: a full policy
/// is the application's to write, with <c>ContentSecurityPolicy</c>).
/// </para>
/// <code>
/// "StaticFiles": {
///   "HashedFilePattern": "-[A-Z2-7]{8}\\.[A-Za-z0-9.]+$", // The default: a regular expression on the file name.
///   "AssetsMaxAge": "01:00:00",                           // A TimeSpan. Default: 0 (no-cache).
///   "ContentSecurityPolicy": "frame-ancestors 'none'",    // The default, on the HTML files. Empty: none.
///   "Headers": { "Permissions-Policy": "camera=()",       // Added to every file...
///                "Referrer-Policy": "" },                 // ...or removed with an empty value.
///   "ContentTypes": { ".custom": "application/x-custom",  // Added...
///                     ".exe": "" },                       // ...or removed with an empty value.
///   "ServeUnknownFileTypes": false,                       // The default.
///   "DefaultContentType": "application/octet-stream",     // The default (with ServeUnknownFileTypes).
///   "RedirectToAppendTrailingSlash": true                 // The default.
/// }
/// </code>
/// </summary>
[PartyClientOption( IsEnabledByDefault = true )]
public sealed partial class StaticFilesOption : TypeScriptClientOption
{
    const string _noCache = "no-cache";
    const string _immutable = "public, max-age=31536000, immutable";
    const string _defaultContentSecurityPolicy = "frame-ancestors 'none'";

    [GeneratedRegex( @"-[A-Z2-7]{8}\.[A-Za-z0-9.]+$", RegexOptions.CultureInvariant )]
    private static partial Regex DefaultHashedFilePattern();

    static readonly string[] _keys = ["HashedFilePattern", "AssetsMaxAge", "ContentSecurityPolicy", "Headers", "ContentTypes",
                                      "ServeUnknownFileTypes", "DefaultContentType", "RedirectToAppendTrailingSlash"];

    Regex _hashedFilePattern = DefaultHashedFilePattern();
    string _assetsCacheControl = _noCache;
    string? _contentSecurityPolicy = _defaultContentSecurityPolicy;
    KeyValuePair<string, string>[] _headers = [];
    readonly FileExtensionContentTypeProvider _contentTypes = new();
    bool _serveUnknownFileTypes;
    string? _defaultContentType;
    bool _redirectToAppendTrailingSlash = true;

    /// <inheritdoc />
    public override bool Initialize( IActivityMonitor monitor, TypeScriptClient client, ImmutableConfigurationSection? configuration )
    {
        var headers = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
        {
            { "X-Content-Type-Options", "nosniff" },
            { "Referrer-Policy", "strict-origin-when-cross-origin" }
        };
        if( configuration != null )
        {
            var path = configuration.Path;
            bool success = true;
            foreach( var c in configuration.GetChildren() )
            {
                if( !_keys.Contains( c.Key ) )
                {
                    monitor.Error( $"Unknown \"{c.Path}\": the keys of \"{path}\" are {_keys.Concatenate()}." );
                    success = false;
                }
            }
            var pattern = configuration["HashedFilePattern"];
            if( pattern != null )
            {
                try
                {
                    _hashedFilePattern = new Regex( pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds( 100 ) );
                }
                catch( ArgumentException ex )
                {
                    monitor.Error( $"Invalid \"{path}:HashedFilePattern\": '{pattern}' is not a regular expression.", ex );
                    success = false;
                }
            }
            var maxAge = configuration["AssetsMaxAge"];
            if( maxAge != null )
            {
                if( TimeSpan.TryParse( maxAge, CultureInfo.InvariantCulture, out var m ) && m >= TimeSpan.Zero )
                {
                    var seconds = (long)m.TotalSeconds;
                    _assetsCacheControl = seconds == 0 ? _noCache : $"public, max-age={seconds}";
                }
                else
                {
                    monitor.Error( $"Invalid \"{path}:AssetsMaxAge\": '{maxAge}' (a positive TimeSpan is expected, like \"01:00:00\")." );
                    success = false;
                }
            }
            var csp = configuration["ContentSecurityPolicy"];
            if( csp != null ) _contentSecurityPolicy = csp.Length == 0 ? null : csp;
            var configuredHeaders = configuration.TryGetSection( "Headers" );
            if( configuredHeaders != null )
            {
                foreach( var h in configuredHeaders.GetChildren() )
                {
                    if( h.Key.Equals( "Cache-Control", StringComparison.OrdinalIgnoreCase )
                        || h.Key.Equals( "Content-Security-Policy", StringComparison.OrdinalIgnoreCase ) )
                    {
                        monitor.Error( $"Invalid \"{h.Path}\": the {h.Key} header is set by the option (use \"AssetsMaxAge\" or \"ContentSecurityPolicy\")." );
                        success = false;
                    }
                    else if( h.Value == null )
                    {
                        monitor.Error( $"Invalid \"{h.Path}\": a header value is expected (empty to remove it)." );
                        success = false;
                    }
                    else if( h.Value.Length == 0 ) headers.Remove( h.Key );
                    else headers[h.Key] = h.Value;
                }
            }
            var contentTypes = configuration.TryGetSection( "ContentTypes" );
            if( contentTypes != null )
            {
                foreach( var t in contentTypes.GetChildren() )
                {
                    if( !t.Key.StartsWith( '.' ) || t.Value == null )
                    {
                        monitor.Error( $"Invalid \"{t.Path}\": an extension (that starts with a dot) and its content type (empty to remove it) are expected." );
                        success = false;
                    }
                    else if( t.Value.Length == 0 ) _contentTypes.Mappings.Remove( t.Key );
                    else _contentTypes.Mappings[t.Key] = t.Value;
                }
            }
            success &= ReadBool( monitor, configuration, "ServeUnknownFileTypes", ref _serveUnknownFileTypes );
            success &= ReadBool( monitor, configuration, "RedirectToAppendTrailingSlash", ref _redirectToAppendTrailingSlash );
            var defaultContentType = configuration["DefaultContentType"];
            if( defaultContentType != null )
            {
                if( defaultContentType.Length > 0 ) _defaultContentType = defaultContentType;
                else
                {
                    monitor.Error( $"Invalid \"{path}:DefaultContentType\": a content type is expected." );
                    success = false;
                }
            }
            if( !success ) return false;
        }
        _headers = headers.ToArray();
        return true;

        static bool ReadBool( IActivityMonitor monitor, ImmutableConfigurationSection c, string key, ref bool value )
        {
            var v = c[key];
            if( v == null ) return true;
            if( bool.TryParse( v, out var b ) )
            {
                value = b;
                return true;
            }
            monitor.Error( $"Invalid \"{c.Path}:{key}\": '{v}' (true or false is expected)." );
            return false;
        }
    }

    /// <summary>
    /// Creates the options of the static file middleware (and of the fallback to "index.html") of a client application.
    /// </summary>
    /// <param name="fileProvider">The built files of the client application.</param>
    /// <param name="requestPath">The request path of the client application.</param>
    /// <returns>The options.</returns>
    public StaticFileOptions CreateStaticFileOptions( IFileProvider fileProvider, PathString requestPath )
    {
        var options = new StaticFileOptions
        {
            FileProvider = fileProvider,
            RequestPath = requestPath,
            ContentTypeProvider = _contentTypes,
            ServeUnknownFileTypes = _serveUnknownFileTypes,
            RedirectToAppendTrailingSlash = _redirectToAppendTrailingSlash,
            OnPrepareResponse = OnPrepareResponse
        };
        if( _defaultContentType != null ) options.DefaultContentType = _defaultContentType;
        return options;
    }

    /// <summary>
    /// Creates the options of the default files middleware (that serves "index.html" for a folder) of a client application.
    /// </summary>
    /// <param name="fileProvider">The built files of the client application.</param>
    /// <param name="requestPath">The request path of the client application.</param>
    /// <returns>The options.</returns>
    public DefaultFilesOptions CreateDefaultFilesOptions( IFileProvider fileProvider, PathString requestPath )
    {
        return new DefaultFilesOptions
        {
            FileProvider = fileProvider,
            RequestPath = requestPath,
            RedirectToAppendTrailingSlash = _redirectToAppendTrailingSlash
        };
    }

    // Called for every response of a file (200, 304, 206...): the content type is set.
    void OnPrepareResponse( StaticFileResponseContext ctx )
    {
        var headers = ctx.Context.Response.Headers;
        var isHtml = ctx.Context.Response.ContentType?.StartsWith( "text/html", StringComparison.OrdinalIgnoreCase ) is true;
        headers.CacheControl = isHtml
                                ? _noCache
                                : _hashedFilePattern.IsMatch( ctx.File.Name ) ? _immutable : _assetsCacheControl;
        foreach( var (name, value) in _headers )
        {
            headers[name] = value;
        }
        if( isHtml && _contentSecurityPolicy != null ) headers.ContentSecurityPolicy = _contentSecurityPolicy;
    }
}

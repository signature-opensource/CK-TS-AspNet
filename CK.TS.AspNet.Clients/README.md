# CK.TS.AspNet.Clients

An ASP.NET host serves its client applications (the Angular applications in the browser) under its own origin: the
authentication requires it (OpenID Connect redirections, passkeys), in production as in development. This package does
it from the host's TypeScript configurations, without any per-application code in the host.

## A client application is a client of the party that declares it

The client applications are clients of their party, in the "PartyClient" feature of
[CK.AppIdentity.PartyClient](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity.PartyClient/README.md):
a client is a bag of client features. A client application served by its party has two of them:

- **its web build** (an `IWebAppBuild` of CK.AppIdentity.PartyClient): "NgApp" for an Angular application
  ([CK.TS.Angular](https://github.com/signature-opensource/CK-TS-Angular/blob/stable/CK.TS.Angular/README.md)): its
  project, its build output and how to start its dev server;
- **its hosting**: the "Hosting" feature of this package, the [`HostingClientFeature`](HostingClientFeature.cs).

```jsonc
"Local": { "PartyClient": {
  "App":     { "NgApp": true, "Hosting": { "RequestPath": "/", "DevServerPort": 4200 } },
  "MiniApp": { "NgApp": true, "Hosting": { "DevServerPort": 4201, "UseDist": true, "StaticFiles": { "AssetsMaxAge": "01:00:00" } } } } }
```

The "Hosting" feature requires the "HttpEndpoint" feature of its party (its endpoint serves the client) and the web
build of the client (`[Requires<HttpEndpointFeatureDriver, IWebAppBuild>]`). Its
[`HostingClientFeatureDriver`](HostingClientFeature.cs) creates it for a client application of the `ClientDirectory`
(see [CK.TypeScript](https://github.com/signature-opensource/CK-TypeScript/blob/stable/CK.TypeScript/README.md#client-applications)):

- **its compile time keys** come from the build (`HostingClientFeature.Info`, a `ClientInfo`): its `RequestPath` (optional:
  `/{kebab-name}` by default, `/` for the default client application: `/mini-app` for the MiniApp) and its
  `DevServerPort`. The Angular engine writes them in the `angular.json`. They reach the run time only when the App keeps
  them in the projection ("ProjectCompileKeys" in the builder's `ckonf.json`), and are then accepted;
- **its run time keys** are `UseDist` and its options (the `StaticFiles` option, below); any other key is an error;
- it gives the request path of the client in the description of its party;
- a client application that the party doesn't declare is neither served nor announced.

## No call serves them

The real object `ClientsHosting` contributes `ClientsApplicationExtensions.EnableClients` to the web applications of
the host (`WebApplicationContributions`, the "Enable" convention of CK.AspNet: it is idempotent): the "HttpEndpoint"
feature of CK.AppIdentity.HttpEndpoint applies it to its endpoint, so a party serves its client applications
without calling anything. `EnableClients` appends `MapClients` to the pipeline; `ClientsHosting` also registers
the HTTP forwarder (the host doesn't call `AddHttpForwarder()`).

`ClientsApplicationExtensions.MapClients` serves each client of the root party that has the "Hosting" feature under its
request path, the default one last:

- **In Development, by its dev server.** The host starts it (its web build's `IWebAppBuild.GetDevServerStart`, `ng serve --port {DevServerPort}` for
  "NgApp", through the yarn found in or above the client's project) unless something already listens on its port (a `yarn start` launched by hand is used
  as is), and stops it with the host. What the host doesn't handle under the request path is forwarded to it (YARP direct
  forwarding): the host stays the origin, the dev server's live reload goes through it too. The dev server compiles
  before it answers: the first requests may fail. A dev server that exits while the host runs is an error in the log,
  with its exit code and its last output lines.
- **Otherwise, by its built files ("dist")**: the static files of `{ContentRoot}/{ProjectPath}/{OutputPath}`, with a
  fallback to its `index.html` for its client routes (a deep link like `/mini-app/some/route`), the caching and the
  security headers of its `StaticFiles` option (below). A client application that is not built is skipped with a warning.

In Development, a client application whose `Hosting:UseDist` is true uses its built files too: this `UseDist` is
its default (a client application nobody develops in this host), the host's configuration overrides it for a run:
`Clients:UseDist=true` for all the client applications (a UI test, a backend-only debugging session),
`Clients:{Name}:UseDist=true|false` for one of them (`dotnet run -- --Clients:MiniApp:UseDist=false`).

## The built files: the `StaticFiles` option

The built files are served as a production server serves them, by the [`StaticFilesOption`](StaticFilesOption.cs) of
each client application: the first option of the "Hosting" feature (a [`HostingClientOption`](HostingClientOption.cs),
see the options of a client feature in
[CK.AppIdentity.PartyClient](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity.PartyClient/README.md)).
It is enabled by default: without configuration, a client application has these defaults.

- **The caching, by kind of file:**
  - the HTML files (`index.html`, also served for the client routes): `Cache-Control: no-cache`, always revalidated (the
    ETag makes it cheap): a new deployment is seen at once;
  - the hashed files, whose name ends with `-` and 8 base32 characters before the extension (`main-VMMCN5NP.js`, the
    names of the Angular builder): `public, max-age=31536000, immutable`;
  - the other files (the copied assets, like `flags/en.svg`, that can change without changing name): `no-cache`.
- **The security headers:** `X-Content-Type-Options: nosniff` and `Referrer-Policy: strict-origin-when-cross-origin` on
  every file; `Content-Security-Policy: frame-ancestors 'none'` on the HTML files (the pages can't be framed). Only this
  directive: a full policy (the scripts, the inline styles) is the application's to write.

Its section, "Local:PartyClient:{Name}:Hosting:StaticFiles", changes the defaults (any other key is an error):

```jsonc
"MiniApp": {
  "StaticFiles": {
    "HashedFilePattern": "-[A-Z2-7]{8}\\.[A-Za-z0-9.]+$", // The default: a regular expression on the file name.
    "AssetsMaxAge": "01:00:00",                           // The other files: public, max-age=3600.
    "ContentSecurityPolicy": "default-src 'self'",        // Replaces the default ("" for none).
    "Headers": { "Permissions-Policy": "camera=()",       // Added to every file...
                 "Referrer-Policy": "" },                 // ...or removed.
    "ContentTypes": { ".custom": "application/x-custom" }, // Extension mappings added (or removed with "").
    "ServeUnknownFileTypes": false,                       // The StaticFileOptions of ASP.NET...
    "DefaultContentType": "application/octet-stream",
    "RedirectToAppendTrailingSlash": true
  }
}
```

`"StaticFiles": false` disables it: the files are served without these headers, when a proxy or a CDN in front sets
them. The file provider and the request path are the client application's, they are not configurable. The option
applies where the "dist" is served: in Development, `Clients:UseDist=true` tests it.

## The running status of a development party

In a development solution, the "DevRunningStatus" feature of
[CK.AppIdentity](https://github.com/signature-opensource/CK-AppIdentity) writes the running status of the party
(`#Dev/Store/.../DevRunningStatus.json`, read by the `#Dev/Start.ps1` launcher). The
[`ClientsDevRunningStatusContributor`](ClientsDevRunningStatusContributor.cs) adds the `Clients` section: the client
applications that `MapClients` serves, with their `Name`, `RequestPath` (the default one is at `/`), `DevServerPort`, effective `UseDist`,
`Serving` and `Error`. A client application that uses its `dist` is serving; one that uses its dev server is serving once
its port answers (it is probed every second while it doesn't). `Error` is null, except when the dev server exited while
the host runs: it is then its exit code and its last output lines, `Serving` is false, and the launcher stops waiting for
this client application.

## The client applications in the description of the party

A client application is described by its `Name`, its `RequestPath` (given by "Hosting") and its facts (given by its
web build: the `[PartyClientFact]` of its selected TypeScript packages): the "PartyClient" feature adds the declared
client applications to the description of the party (its `IParty.Description`, see the "PartyDescription" feature of
[CK.AppIdentity](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity/README.md#the-description-of-a-party)):
the party announces them to its remotes, and the links that a remote builds for it (the mails of an Authentication
Center) target them (`PartyDescription.GetClientApplicationUrl`).

## Requires.

- [CK.TypeScript](https://github.com/signature-opensource/CK-TypeScript/blob/stable/CK.TypeScript/README.md) for the `ClientDirectory`.
- [CK.AspNet](https://github.com/signature-opensource/CK-AspNet/blob/stable/CK.AspNet/README.md#the-enable-convention-and-the-contributions-of-the-packages)
  for the "Enable" convention and the contributions.
- [CK.AppIdentity.PartyClient](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity.PartyClient/README.md)
  for the "PartyClient" feature, and CK.AppIdentity.HttpEndpoint for the "HttpEndpoint" feature that serves them.
- [Yarp.ReverseProxy](https://github.com/dotnet/yarp) for the forwarding to the dev servers.

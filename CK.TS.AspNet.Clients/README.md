# CK.TS.AspNet.Clients

An ASP.NET host serves its client applications (the Angular applications in the browser) under its own origin: the
authentication requires it (OpenID Connect redirections, passkeys), in production as in development. This package does
it from the host's TypeScript configurations, without any per-application code in the host.

## The client applications come from the configuration

A `<TypeScript>` configuration with a `Kind` is a client application (see
[CK.TypeScript](../CK.TypeScript/README.md#client-applications)):

```xml
<TypeScript Name="App" Kind="DefaultApp" DevServerPort="4200" />
<TypeScript Name="MiniApp" Kind="App" DevServerPort="4201" UseDist="true" />
```

The engine generates the `ClientDirectory` of the host from them: each `ClientInfo` has its `Name`, its `RequestPath`
(`/` for the default one, `/mini-app` here), its `ProjectPath` (`Clients/MiniApp`, relative to the host's project), its
`OutputPath` (`dist/MiniApp/browser`), its `TypeFilterName`, its `DevServerPort` and its `UseDist`.

## One call serves them

```csharp
var app = builder.CKBuild( map );
app.UseCris();
app.MapClients();
```

`ClientsApplicationExtensions.MapClients` serves each client application under its request path, the default one last:

- **In Development, by its dev server.** The host starts it (`ng serve --port {DevServerPort}`, through the yarn found in
  or above the client's project) unless something already listens on its port (a `yarn start` launched by hand is used
  as is), and stops it with the host. What the host doesn't handle under the request path is forwarded to it (YARP direct
  forwarding): the host stays the origin, the dev server's live reload goes through it too. The dev server compiles
  before it answers: the first requests may fail. A dev server that exits while the host runs is an error in the log,
  with its exit code and its last output lines.
- **Otherwise, by its built files ("dist")**: the static files of `{ContentRoot}/{ProjectPath}/{OutputPath}`, with a
  fallback to its `index.html` for its client routes (a deep link like `/mini-app/some/route`). A client application that
  is not built is skipped with a warning.

In Development, a client application whose `UseDist` is true uses its built files too: the configuration's `UseDist` is
its default (a client application nobody develops in this host), the host's configuration overrides it for a run:
`Clients:UseDist=true` for all the client applications (a UI test, a backend-only debugging session),
`Clients:{Name}:UseDist=true|false` for one of them (`dotnet run -- --Clients:MiniApp:UseDist=false`).

The real object `ClientsHosting` registers the HTTP forwarder: the host doesn't call `AddHttpForwarder()`.

## Or no call at all

`ClientsApplicationExtensions.EnableClients` is the same in one gesture on the `WebApplicationBuilder` (the "Enable"
convention of CK.AspNet: it is idempotent). `ClientsHosting` contributes it to the web applications of the host
(`WebApplicationContributions`): a host that applies the contributions, like the "HttpEndpoint" feature of
CK.AppIdentity.HttpEndpoint, serves the client applications without calling anything.

## The running status of a development party

In a development solution, the "DevRunningStatus" feature of
[CK.AppIdentity](https://github.com/signature-opensource/CK-AppIdentity) writes the running status of the party
(`#Dev/Store/.../DevRunningStatus.json`, read by the `#Dev/Start.ps1` launcher). The
[`ClientsDevRunningStatusContributor`](ClientsDevRunningStatusContributor.cs) adds the `Clients` section: the client
applications that `MapClients` serves, with their `Name`, `Kind`, `RequestPath`, `DevServerPort`, effective `UseDist`,
`Serving` and `Error`. A client application that uses its `dist` is serving; one that uses its dev server is serving once
its port answers (it is probed every second while it doesn't). `Error` is null, except when the dev server exited while
the host runs: it is then its exit code and its last output lines, `Serving` is false, and the launcher stops waiting for
this client application.

## The client applications in the description of the party

The [`ClientsPartyDescriptionContributor`](ClientsPartyDescriptionContributor.cs) adds the client applications of the
`ClientDirectory` (their `Name` and `RequestPath`) to the description of the party (its `IParty.Description`, see the
"PartyDescription" feature of [CK.AppIdentity](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity/README.md#the-description-of-a-party)):
the party announces them to its remotes, and the links that a remote builds for it (the mails of an Authentication
Center) target them (`PartyDescription.GetClientApplicationUrl`).

## Requires.

- [CK.TypeScript](../CK.TypeScript/README.md) for the `ClientDirectory`.
- [CK.AspNet](https://github.com/signature-opensource/CK-AspNet/blob/stable/CK.AspNet/README.md#the-enable-convention-and-the-contributions-of-the-packages)
  for the "Enable" convention and the contributions.
- [Yarp.ReverseProxy](https://github.com/dotnet/yarp) for the forwarding to the dev servers.

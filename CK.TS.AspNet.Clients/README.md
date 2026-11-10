# CK.TS.AspNet.Clients

An ASP.NET host serves its client applications (the Angular applications in the browser) under its own origin: the
authentication requires it (OpenID Connect redirections, passkeys), in production as in development. This package does
it from the host's TypeScript configurations, without any per-application code in the host.

## The client applications come from the configuration

A `<TypeScript>` configuration with a `Kind` is a client application (see
[CK.TypeScript](https://github.com/signature-opensource/CK-TypeScript/blob/stable/CK.TypeScript/README.md#client-applications)):

```xml
<TypeScript Name="App" Kind="DefaultApp" DevServerPort="4200" />
<TypeScript Name="MiniApp" Kind="App" DevServerPort="4201" UseDist="true" />
```

The engine generates the `ClientDirectory` of the host from them: each `ClientInfo` has its `Name`, its `RequestPath`
(`/` for the default one, `/mini-app` here), its `ProjectPath` (`Clients/MiniApp`, relative to the host's project), its
`OutputPath` (`dist/MiniApp/browser`), its `TypeFilterName`, its `DevServerPort` and its `UseDist`.

## A client application is a client of the party that declares it

The client applications are the clients of their party, in the "PartyClient" feature of
[CK.AppIdentity.PartyClient](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity.PartyClient/README.md):
the build says which client applications exist, the party's configuration says which ones it uses.

```jsonc
"CK-AppIdentity": { "Local": { "PartyClient": { "App": {}, "MiniApp": {} } } }
```

The [`TypeScriptClientFactory`](TypeScriptClientFactory.cs) creates the [`TypeScriptClient`](TypeScriptClient.cs) of a
party that declares a client application of the `ClientDirectory` (its `Info` is its `ClientInfo`):
- a client application requires the "HttpEndpoint" feature of its party (its endpoints serve it);
- everything it is comes from the build: its declaration has no fact, only the options of `TypeScriptClient` (any other
  key in "Local:PartyClient:{Name}" is an error);
- a client application that the party doesn't declare is neither served nor announced.

The engine of CK.TypeScript adds the missing declarations of the root party to the `appsettings.json` of a host project
(a temporary help, see [CK.TypeScript](https://github.com/signature-opensource/CK-TypeScript/blob/stable/CK.TypeScript/README.md)).

## No call serves them

The real object `ClientsHosting` contributes `ClientsApplicationExtensions.EnableClients` to the web applications of
the host (`WebApplicationContributions`, the "Enable" convention of CK.AspNet: it is idempotent): the "HttpEndpoint"
feature of CK.AppIdentity.HttpEndpoint applies it to each of its endpoints, so a party serves its client applications
without calling anything. `EnableClients` appends `MapClients` to the pipeline; `ClientsHosting` also registers
the HTTP forwarder (the host doesn't call `AddHttpForwarder()`).

`ClientsApplicationExtensions.MapClients` serves each client application that the root party declares under its request
path, the default one last:

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

A `TypeScriptClient` is described by its `Name`, its `RequestPath` and its facts (`ClientInfo.Facts`: the
`[PartyClientFact]` of its selected TypeScript packages): the "PartyClient" feature adds the declared
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

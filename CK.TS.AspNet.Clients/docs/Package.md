The "Hosting" client feature: a party serves its client applications under the origin of its HTTP endpoint, their
built files in production and their dev server in development.

The clients of a party that have the "Hosting" feature (`HostingClientFeature`) are served by
`ClientsApplicationExtensions.MapClients`: their built files ("dist") in production; in Development it starts their dev
server (the `IWebAppBuild` of the client: "NgApp" for an Angular application) and forwards to it, unless their "dist" is
used (`HostingClientFeature.UseDist`, that the "Clients:{Name}:UseDist" or "Clients:UseDist" configuration overrides).
The built files are served with cache and security headers (the StaticFiles option of the "Hosting" feature). A client
is declared by its party ("CK-AppIdentity:Local:PartyClient:{Name}", the "PartyClient" feature of
CK.AppIdentity.PartyClient).

See the [README](https://github.com/signature-opensource/CK-TS-AspNet/blob/stable/CK.TS.AspNet.Clients/README.md).

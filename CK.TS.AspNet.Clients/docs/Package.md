The "Hosting" client feature: serves the client applications of a party (its clients that have the "Hosting" feature,
HostingClientFeature) under the origin of its HTTP endpoint: pp.MapClients() serves their built files ("dist") in
production, and in Development starts their dev server (the IWebAppBuild of the client: "NgApp" for an Angular
application) and forwards to it, unless their "dist" is used ("Hosting:UseDist", Clients:UseDist). The built files
are served with cache and security headers (the StaticFiles option of the "Hosting" feature). A client is declared by
its party ("CK-AppIdentity:Local:PartyClient:{Name}", the "PartyClient" feature of CK.AppIdentity.PartyClient).

# CK-TS-AspNet

What the TypeScript engine brings to an ASP.NET host: serving its client applications, and the TypeScript client of
its WebSocket channel.

| Package | Description | Latest stable |
|---------|-------------|---------------|
| [CK.TS.AspNet.Clients](CK.TS.AspNet.Clients/README.md) | Serves the client applications of an ASP.NET host under the host's origin: their built files in production, their dev server in development. | [![nuget](https://img.shields.io/nuget/v/CK.TS.AspNet.Clients.svg?label=CK.TS.AspNet.Clients)](https://www.nuget.org/packages/CK.TS.AspNet.Clients/) |
| [CK.TS.AspNet.WebSocketChannel](CK.TS.AspNet.WebSocketChannel) | TypeScript client of the application-wide WebSocket channel: `WSConnection` opens the one socket, reconnects, and routes each frame to the handler registered for its topic. | [![nuget](https://img.shields.io/nuget/v/CK.TS.AspNet.WebSocketChannel.svg?label=CK.TS.AspNet.WebSocketChannel)](https://www.nuget.org/packages/CK.TS.AspNet.WebSocketChannel/) |
| [CK.TS.AspNet.Auth](CK.TS.AspNet.Auth/README.md) | Deprecated: replaced by CK.Cris.AspNet.Authentication (its `AuthService.ts`). | |

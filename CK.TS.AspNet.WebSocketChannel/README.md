# CK.TS.AspNet.WebSocketChannel

The features of a client application that need messages pushed by the server share one WebSocket rather than each
opening its own: the client holds one connection whatever the number of features. This package is the TypeScript
client of that channel, `WSConnection`: it opens the socket, keeps its connection identifier, reconnects, and routes
each frame to the handler registered for its topic. It needs nothing from the server package: the server side is
[CK.AspNet.WebSocketChannel](https://github.com/signature-opensource/CK-AspNet/blob/stable/CK.AspNet.WebSocketChannel/README.md),
which serves the channel (on `/ws` by default).

## The package

[`WebSocketChannelPackage`](WebSocketChannelPackage.cs) is a TypeScript package: it adds
[`Res/ws-connection.ts`](Res/ws-connection.ts) to the generated code, which exports `WSConnection` and
`WSTopicHandler`. The assembly is `CK.TS.AspNet.WebSocketChannel`, its namespace is `CK.AspNet.WebSocketChannel`.

The client is feature agnostic: what a topic means is the business of whoever registers it.

## The connection

`WSConnection` is created with the URL of the socket (`new WSConnection( url )`), and nothing is opened until
`WSConnection.start` is called:

| Member | |
|--------|-|
| `start()` | Opens the connection. Idempotent: it does nothing on an open connection. |
| `connectionId` | The connection identifier, `undefined` while no connection is established. |
| `whenConnectedAsync()` | Resolves with the connection identifier: at once when a connection is established, on the next one otherwise. It doesn't open the connection. |
| `addHandler( topic, handler )` | Registers the `WSTopicHandler` of a topic. One handler per topic: registering again silently replaces the previous one. |
| `removeHandler( topic )` | Unregisters a topic: its messages are ignored and its callbacks stop firing. |
| `stopAsync()` | Closes the connection and cancels any pending reconnection. This closes the socket of every feature: a feature that is done removes its handler instead. |

The socket carries no credential: it is opened anonymously, and the first frame the server sends is the negotiation
`{ connectionId }`. A feature that needs an identity binds it afterwards, by sending this identifier through an
authenticated Cris command. The client sends nothing on the socket.

The other frames are `{ topic, message }` envelopes: the `message` of an envelope goes, as is, to the handler of its
`topic`. A frame that is not JSON, an envelope without a topic, or a topic without a handler is ignored with a console
message.

## The handler of a topic

```ts
export interface WSTopicHandler {
  onMessage( message: unknown ): void;
  onConnected?( connectionId: string ): void;
  onClosed?( error: Error | undefined ): void;
}
```

- `WSTopicHandler.onMessage` receives each message of the topic: the payload is never interpreted by the connection.
- `WSTopicHandler.onConnected` is called with the connection identifier on every (re)connection while the topic is
  registered, and right away when the topic is registered on an established connection. This is where a feature
  (re)negotiates: the identifier it had is gone with the previous socket.
- `WSTopicHandler.onClosed` is called when the connection is lost, with an `Error` (the close reason) when the close was
  not clean. `WSConnection.stopAsync` doesn't raise it.

Topics are one flat namespace shared by every feature on the socket, and a registration silently replaces the previous
one: name a topic after its package.

A handler that throws is logged to the console and doesn't prevent the other topics from being routed or notified.

## Reconnection

A lost connection is reconnected after a delay that starts at 1 second, doubles on each failed attempt, and is capped
at 30 seconds. The delay is reset only when a negotiation completes. The events of a socket that a reconnection has
replaced are ignored.

## Tests

The jest tests of `WSConnection`, in [CK.TS.AspNet.WebSocketChannel.Tests](../Tests/CK.TS.AspNet.WebSocketChannel.Tests),
exercise it against a fake WebSocket installed on the global: no server is started.

## Requires

- [CK.TypeScript](https://github.com/signature-opensource/CK-TypeScript/blob/stable/CK.TypeScript/README.md) for the
  TypeScript package.

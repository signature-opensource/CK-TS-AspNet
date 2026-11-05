TypeScript client of the application-wide WebSocket channel served by
[CK.AspNet.WebSocketChannel](https://github.com/signature-opensource/CK-AspNet/blob/stable/CK.AspNet.WebSocketChannel/README.md):
the `WebSocketChannelPackage` adds `WSConnection` and `WSTopicHandler` to the generated TypeScript code. `WSConnection`
opens the one socket of the application, keeps its connection identifier, reconnects with a backoff, and routes each
`{ topic, message }` frame to the `WSTopicHandler` registered for its topic.
See the [README](https://github.com/signature-opensource/CK-TS-AspNet/blob/stable/CK.TS.AspNet.WebSocketChannel/README.md).

using CK.TypeScript;

namespace CK.AspNet.WebSocketChannel;

/// <summary>
/// TypeScript package that exposes the <c>WSConnection</c> client to generated TypeScript clients.
/// <para>
/// The client is feature agnostic: it opens the socket, keeps its connection identifier, reconnects,
/// and routes each message to the handler registered for its topic. What a topic means is the
/// business of whoever registers it.
/// </para>
/// </summary>
[TypeScriptPackage]
[TypeScriptFile( "ws-connection.ts", "WSConnection", "WSTopicHandler" )]
public class WebSocketChannelPackage : TypeScriptPackage
{
}

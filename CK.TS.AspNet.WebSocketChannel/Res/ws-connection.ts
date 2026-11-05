/**
 * What a feature registers for its topic: the messages, and the two moments of the connection it may
 * need to react to.
 *
 * The lifecycle callbacks live here rather than in a separate subscription so that
 * {@link WSConnection.removeHandler} releases them along with the topic: a feature that has stopped
 * listening must stop being notified, and there is nothing else to unregister.
 */
export interface WSTopicHandler {
  /**
   * Called for each message received under the topic. The payload is whatever the server sent: this
   * connection never interprets it, so a feature receives exactly the shape it already knows.
   */
  onMessage( message: unknown ): void;

  /**
   * Called with the connection identifier on every (re)connection while the topic is registered.
   *
   * This is where a feature re-negotiates. Not on the socket - the channel is descending only - but by
   * sending a Cris command carrying the identifier: the previous one died with the previous socket, so
   * whatever the server knew about this client has to be established again.
   */
  onConnected?( connectionId: string ): void;

  /**
   * Called when the connection is lost while the topic is registered, with the reason when the close
   * was not clean. A deliberate {@link WSConnection.stopAsync} does not raise it.
   */
  onClosed?( error: Error | undefined ): void;
}

// Reconnection backoff: doubles on each failed attempt, capped. There is no point hammering a server
// that is down.
const RECONNECT_MIN_MS = 1000;
const RECONNECT_MAX_MS = 30000;

// The two frames a server can send. They are narrowed by these guards rather than described by a type
// annotation on the parse result: JSON.parse returns any, so an annotation would state a hope and check
// nothing - it lets a null or a number through, and the first property access throws. Taking the parse
// result as unknown is what makes the compiler require these.
//
// Only the envelope is guarded, not the payload: the envelope is shared and decides where the bytes go,
// so a malformed one must not reach a handler. What is inside belongs to one feature and degrades on
// its own.

/** The handshake: the first frame of a connection, and the only one carrying no topic. */
function isNegotiation( o: unknown ): o is { connectionId: string } {
  return typeof o === 'object' && o !== null
    && typeof ( o as { connectionId?: unknown } ).connectionId === 'string';
}

/** An application frame: a topic to route on, and a payload this class never interprets. */
function isEnvelope( o: unknown ): o is { topic: string; message: unknown } {
  return typeof o === 'object' && o !== null
    && typeof ( o as { topic?: unknown } ).topic === 'string';
}

/**
 * The one WebSocket connection of the application.
 *
 * Every feature shares this socket and is routed by topic: a feature registers a handler with
 * {@link addHandler} and receives the messages the server sends under that topic. Nothing else opens
 * a socket, so a client holds one connection whatever the number of features.
 *
 * The socket itself carries no credential: it is opened anonymously and the server answers with a
 * connection identifier. Whoever needs an identity binds it afterwards, by sending that identifier
 * through the authenticated Cris endpoint. No token ever transits in the socket URL.
 *
 * This object owns the reconnection. That is what makes the channel self-healing, and it is why
 * {@link WSTopicHandler.onConnected} fires again on every reconnection rather than once: a feature
 * must re-negotiate there, because the identifier it had is gone with the previous socket.
 */
export class WSConnection {
  // The one registry of this class: a topic, and everything the feature behind it asked to know.
  readonly #handlers = new Map<string, WSTopicHandler>();
  // Resolvers of the whenConnectedAsync() calls made while no connection was established. Not a
  // subscription: one-shot, and cleared as soon as they are resolved.
  #connectedWaiters: Array<( connectionId: string ) => void> = [];

  #socket?: WebSocket;
  #connectionId?: string;
  // True between stopAsync() and the next start(): tells the close handler not to reconnect.
  #stopped = true;
  #reconnectDelay = RECONNECT_MIN_MS;
  #reconnectTimer?: ReturnType<typeof setTimeout>;

  constructor( private readonly url: string ) { }

  /** The current connection identifier, undefined while no connection is established. */
  get connectionId(): string | undefined {
    return this.#connectionId;
  }

  /**
   * Registers the handler of one topic. One handler per topic: registering again replaces the previous
   * one, which is what lets a feature call this on every reconnection without accumulating.
   *
   * Topics live in one flat namespace shared by every feature on this socket, and replacing is silent -
   * it has to be, since a feature cannot be told apart from itself re-registering. So a topic must be
   * globally unique: **name it after your package**. Anything shorter eventually collides with someone else, and a
   * collision shows up as another feature's payloads arriving at your handler.
   *
   * When a connection is already established, onConnected is called right away. Without this a feature
   * registering on a live connection would hear nothing until the next reconnection, and would have to
   * catch that case up by hand - a trap nobody should have to know about, since onConnected is where a
   * feature negotiates.
   */
  addHandler( topic: string, handler: WSTopicHandler ): void {
    this.#handlers.set( topic, handler );
    if ( this.#connectionId !== undefined ) {
      try {
        handler.onConnected?.( this.#connectionId );
      } catch ( e ) {
        console.error( `WSConnection: onConnected of topic '${topic}' threw.`, e );
      }
    }
  }

  /**
   * Unregisters one topic: its messages are ignored from now on, and its connection callbacks stop
   * firing. A feature that is done calls this - it never closes the connection, which is shared.
   */
  removeHandler( topic: string ): void {
    this.#handlers.delete( topic );
  }

  /** Opens the connection. Idempotent: calling it on an open connection does nothing. */
  start(): void {
    if ( !this.#stopped ) return;
    this.#stopped = false;
    this.#reconnectDelay = RECONNECT_MIN_MS;
    this.#connect();
  }

  /**
   * Resolves with the connection identifier: immediately if a connection is established, on the next
   * one otherwise. Does not open the connection - {@link start} does, and the application initializer
   * of this package calls it before anything else is initialized.
   */
  whenConnectedAsync(): Promise<string> {
    if ( this.#connectionId !== undefined ) return Promise.resolve( this.#connectionId );
    return new Promise<string>( resolve => this.#connectedWaiters.push( resolve ) );
  }

  /**
   * Closes the connection and cancels any pending reconnection. This kills the socket of every
   * feature: only the application shuts the channel down, a feature that is done merely removes its
   * handler.
   */
  async stopAsync(): Promise<void> {
    this.#stopped = true;
    if ( this.#reconnectTimer !== undefined ) {
      clearTimeout( this.#reconnectTimer );
      this.#reconnectTimer = undefined;
    }
    const socket = this.#socket;
    // Cleared before closing, so the close handler sees an obsolete socket and does not raise
    // onClosed: a deliberate stop is not a connection loss.
    this.#socket = this.#connectionId = undefined;
    if ( !socket ) return;
    await new Promise<void>( resolve => {
      // Resolve on close rather than immediately: a half-closed socket would still deliver messages.
      socket.addEventListener( 'close', () => resolve(), { once: true } );
      try {
        socket.close();
      } catch ( e ) {
        console.error( e );
        resolve();
      }
    } );
  }

  #connect(): void {
    this.#connectionId = undefined;
    let socket: WebSocket;
    try {
      socket = new WebSocket( this.url );
    } catch ( e ) {
      // A malformed URL or a blocked scheme: retrying cannot hurt, and stopping silently would leave
      // the channel dead with no trace.
      console.error( e );
      this.#scheduleReconnect();
      return;
    }
    this.#socket = socket;
    socket.onmessage = event => this.#onMessageEvent( socket, event );
    // Same staleness guard as the other two handlers: an error on a socket already replaced would
    // otherwise be reported as if it were the current channel. Nothing else here - onerror is always
    // followed by onclose, and reconnection is handled there only, so that a single failure cannot
    // schedule two attempts.
    socket.onerror = () => {
      if ( socket !== this.#socket ) return;
      console.warn( 'WSConnection: socket error.' );
    };
    socket.onclose = event => this.#onCloseEvent( socket, event );
  }

  #onMessageEvent( socket: WebSocket, event: MessageEvent ): void {
    // A late message from a socket already replaced by a reconnection must be ignored.
    if ( socket !== this.#socket ) return;
    let data: unknown;
    try {
      data = JSON.parse( event.data );
    } catch ( e ) {
      console.error( 'WSConnection: unparseable message.', e );
      return;
    }
    // The negotiation is the first frame of a connection: once we have an identifier, anything that
    // looks like one again is stale and falls through to the warning below.
    if ( this.#connectionId === undefined && isNegotiation( data ) ) {
      this.#onNegotiated( data.connectionId );
      return;
    }
    if ( !isEnvelope( data ) ) {
      console.warn( 'WSConnection: message without a topic, ignored.' );
      return;
    }
    const handler = this.#handlers.get( data.topic );
    if ( !handler ) {
      console.warn( `WSConnection: no handler for topic '${data.topic}', message ignored.` );
      return;
    }
    try {
      handler.onMessage( data.message );
    } catch ( e ) {
      // One faulty feature must not silence the other topics of the shared socket.
      console.error( `WSConnection: handler of topic '${data.topic}' threw.`, e );
    }
  }

  #onNegotiated( connectionId: string ): void {
    this.#connectionId = connectionId;
    // A completed negotiation is what proves the server healthy: reset the backoff only here.
    this.#reconnectDelay = RECONNECT_MIN_MS;
    const waiters = this.#connectedWaiters;
    this.#connectedWaiters = [];
    for ( const resolve of waiters ) {
      resolve( connectionId );
    }
    this.#notify( ( topic, handler ) => handler.onConnected?.( connectionId ), 'onConnected' );
  }

  #onCloseEvent( socket: WebSocket, event: CloseEvent ): void {
    if ( socket !== this.#socket ) return; // Close of a socket already replaced, or a deliberate stop.
    this.#socket = this.#connectionId = undefined;
    const error = event.wasClean ? undefined : new Error( event.reason );
    this.#notify( ( topic, handler ) => handler.onClosed?.( error ), 'onClosed' );
    if ( this.#stopped ) return;
    this.#scheduleReconnect();
  }

  // Iterates over a snapshot: a handler is free to add or remove a topic while being notified.
  #notify( action: ( topic: string, handler: WSTopicHandler ) => void, name: string ): void {
    for ( const [topic, handler] of [...this.#handlers] ) {
      try {
        action( topic, handler );
      } catch ( e ) {
        // Same rule as for messages: one faulty feature keeps its problem to itself.
        console.error( `WSConnection: ${name} of topic '${topic}' threw.`, e );
      }
    }
  }

  #scheduleReconnect(): void {
    if ( this.#stopped || this.#reconnectTimer !== undefined ) return;
    const delay = this.#reconnectDelay;
    this.#reconnectDelay = Math.min( delay * 2, RECONNECT_MAX_MS );
    this.#reconnectTimer = setTimeout( () => {
      this.#reconnectTimer = undefined;
      if ( !this.#stopped ) this.#connect();
    }, delay );
  }
}

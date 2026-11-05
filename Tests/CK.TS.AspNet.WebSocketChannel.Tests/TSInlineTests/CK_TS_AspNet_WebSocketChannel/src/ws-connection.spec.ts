import { WSConnection } from '@local/ck-gen';

// Trick from https://stackoverflow.com/a/77047461/190380
// When debugging ("Debug Test at Cursor" in menu), this cancels jest timeout.
if ( process.env["VSCODE_INSPECTOR_OPTIONS"] ) jest.setTimeout( 30 * 60 * 1000 );

/**
 * A WebSocket we drive by hand. WSConnection reads the constructor from the global, so replacing it is
 * all it takes: no server, no network, and every event happens exactly when the test says so.
 */
class FakeWebSocket {
    static instances: FakeWebSocket[] = [];

    static get last(): FakeWebSocket {
        const last = FakeWebSocket.instances[FakeWebSocket.instances.length - 1];
        if ( !last ) throw new Error( 'No socket has been created.' );
        return last;
    }

    static reset(): void {
        FakeWebSocket.instances = [];
        FakeWebSocket.attempts = 0;
        FakeWebSocket.throwOnConstruct = false;
    }

    // Counted before the throw, so a construction that fails is still observable - which is the only
    // way to watch the backoff of an url that can never be opened.
    static attempts = 0;

    // Reproduces a constructor that throws: a malformed url or a scheme the browser refuses.
    static throwOnConstruct = false;

    onmessage: ( ( e: unknown ) => void ) | null = null;
    onerror: ( () => void ) | null = null;
    onclose: ( ( e: unknown ) => void ) | null = null;
    closeCalled = false;

    readonly #closeListeners: Array<() => void> = [];

    constructor( readonly url: string ) {
        FakeWebSocket.attempts++;
        if ( FakeWebSocket.throwOnConstruct ) throw new Error( 'Refused.' );
        FakeWebSocket.instances.push( this );
    }

    addEventListener( type: string, handler: () => void ): void {
        if ( type === 'close' ) this.#closeListeners.push( handler );
    }

    close(): void {
        this.closeCalled = true;
        this.emitClose( true, '' );
    }

    /** Delivers a frame, serialized as the server would. */
    emitFrame( frame: unknown ): void {
        this.onmessage?.( { data: JSON.stringify( frame ) } );
    }

    /** Delivers raw text, to exercise what a broken or hostile peer could send. */
    emitRaw( data: string ): void {
        this.onmessage?.( { data } );
    }

    emitClose( wasClean = false, reason = 'lost' ): void {
        this.onclose?.( { wasClean, reason } );
        for ( const l of this.#closeListeners ) l();
    }

    emitError(): void {
        this.onerror?.();
    }
}

// Registers a topic and records everything it is told.
function record( c: WSConnection, topic: string ) {
    const messages: Array<unknown> = [];
    const connected: Array<string> = [];
    const closed: Array<Error | undefined> = [];
    c.addHandler( topic, {
        onMessage: m => messages.push( m ),
        onConnected: id => connected.push( id ),
        onClosed: e => closed.push( e )
    } );
    return { messages, connected, closed };
}

describe( 'WSConnection', () => {

    const globals = globalThis as unknown as Record<string, unknown>;
    const realWebSocket = globals['WebSocket'];

    beforeEach( () => {
        FakeWebSocket.reset();
        globals['WebSocket'] = FakeWebSocket;
        jest.spyOn( console, 'warn' ).mockImplementation( () => { } );
        jest.spyOn( console, 'error' ).mockImplementation( () => { } );
    } );

    afterEach( () => {
        globals['WebSocket'] = realWebSocket;
        jest.restoreAllMocks();
    } );

    // Opens a connection and negotiates it, which is the starting point of most tests.
    function connected( url = '/ws' ) {
        const c = new WSConnection( url );
        c.start();
        FakeWebSocket.last.emitFrame( { connectionId: 'C1' } );
        return c;
    }

    it( 'negotiates: the identifier is exposed and onConnected fires with it', () => {
        const c = new WSConnection( '/ws' );
        const t = record( c, 'OD' );
        c.start();

        expect( c.connectionId ).toBeUndefined();
        expect( t.connected ).toEqual( [] );

        FakeWebSocket.last.emitFrame( { connectionId: 'C1' } );

        expect( c.connectionId ).toBe( 'C1' );
        expect( t.connected ).toEqual( ['C1'] );
    } );

    it( 'calls onConnected right away when registering on a live connection', () => {
        const c = connected();
        // The trap this removes: without it a feature registering here would negotiate nothing until
        // the next reconnection.
        const late = record( c, 'SC' );
        expect( late.connected ).toEqual( ['C1'] );
    } );

    it( 'routes a message to its topic only, and hands the payload over untouched', () => {
        const c = connected();
        const od = record( c, 'OD' );
        const sc = record( c, 'SC' );

        const frame = ['D', { N: 12, E: [['I', 1]], L: 11 }];
        FakeWebSocket.last.emitFrame( { topic: 'OD', message: frame } );
        FakeWebSocket.last.emitFrame( { topic: 'SC', message: { type: 'banned' } } );

        expect( od.messages ).toEqual( [frame] );
        expect( sc.messages ).toEqual( [{ type: 'banned' }] );
    } );

    it( 'start() is idempotent: it does not open a second socket', () => {
        const c = connected();
        c.start();
        expect( FakeWebSocket.instances.length ).toBe( 1 );
    } );

    describe( 'removeHandler', () => {

        it( 'stops messages and connection callbacks alike', () => {
            const c = connected();
            const t = record( c, 'OD' );
            c.removeHandler( 'OD' );

            FakeWebSocket.last.emitFrame( { topic: 'OD', message: 1 } );
            FakeWebSocket.last.emitClose();

            expect( t.messages ).toEqual( [] );
            expect( t.closed ).toEqual( [] );
        } );

        it( 'leaves the other topics untouched', () => {
            const c = connected();
            const od = record( c, 'OD' );
            const sc = record( c, 'SC' );
            c.removeHandler( 'OD' );

            FakeWebSocket.last.emitFrame( { topic: 'SC', message: 'still here' } );

            expect( od.messages ).toEqual( [] );
            expect( sc.messages ).toEqual( ['still here'] );
        } );
    } );

    describe( 'hostile frames', () => {

        // A frame is parsed before anything knows what it is: a bad one must not take the socket - and
        // therefore every other feature - down with it.
        it.each( [
            ['not json at all', 'unparseable'],
            ['null', 'null'],
            ['42', 'a number'],
            ['"text"', 'a string'],
            ['{"message":1}', 'no topic'],
            ['{"topic":12,"message":1}', 'a non-string topic']
        ] )( 'ignores %s (%s) and keeps serving', ( raw ) => {
            const c = connected();
            const t = record( c, 'OD' );

            expect( () => FakeWebSocket.last.emitRaw( raw ) ).not.toThrow();
            expect( t.messages ).toEqual( [] );

            // The connection is still usable, which is the whole point.
            FakeWebSocket.last.emitFrame( { topic: 'OD', message: 'ok' } );
            expect( t.messages ).toEqual( ['ok'] );
        } );

        it( 'ignores a message on an unregistered topic', () => {
            const c = connected();
            expect( () => FakeWebSocket.last.emitFrame( { topic: 'NOBODY', message: 1 } ) ).not.toThrow();
        } );

        it( 'a second negotiation frame does not change the identifier', () => {
            const c = connected();
            FakeWebSocket.last.emitFrame( { connectionId: 'C2' } );
            expect( c.connectionId ).toBe( 'C1' );
        } );
    } );

    describe( 'a faulty feature', () => {

        it( 'does not prevent the other topics from receiving', () => {
            const c = connected();
            c.addHandler( 'BAD', { onMessage: () => { throw new Error( 'boom' ); } } );
            const good = record( c, 'OD' );

            expect( () => FakeWebSocket.last.emitFrame( { topic: 'BAD', message: 1 } ) ).not.toThrow();
            FakeWebSocket.last.emitFrame( { topic: 'OD', message: 'ok' } );

            expect( good.messages ).toEqual( ['ok'] );
        } );

        it( 'throwing from onConnected does not stop the others being notified', () => {
            const c = new WSConnection( '/ws' );
            c.addHandler( 'BAD', { onMessage: () => { }, onConnected: () => { throw new Error( 'boom' ); } } );
            const good = record( c, 'OD' );
            c.start();

            expect( () => FakeWebSocket.last.emitFrame( { connectionId: 'C1' } ) ).not.toThrow();
            expect( good.connected ).toEqual( ['C1'] );
        } );
    } );

    describe( 'whenConnectedAsync', () => {

        it( 'resolves at once when already connected', async () => {
            const c = connected();
            await expect( c.whenConnectedAsync() ).resolves.toBe( 'C1' );
        } );

        it( 'resolves on the next negotiation otherwise', async () => {
            const c = new WSConnection( '/ws' );
            c.start();
            const pending = c.whenConnectedAsync();
            FakeWebSocket.last.emitFrame( { connectionId: 'C1' } );
            await expect( pending ).resolves.toBe( 'C1' );
        } );
    } );

    describe( 'reconnection', () => {

        beforeEach( () => jest.useFakeTimers() );
        afterEach( () => jest.useRealTimers() );

        it( 'reports the loss then reconnects, and re-negotiates the new identifier', () => {
            const c = connected();
            const t = record( c, 'OD' );

            FakeWebSocket.last.emitClose( false, 'lost' );
            expect( t.closed.length ).toBe( 1 );
            expect( t.closed[0] ).toBeInstanceOf( Error );
            expect( c.connectionId ).toBeUndefined();

            jest.advanceTimersByTime( 1000 );
            expect( FakeWebSocket.instances.length ).toBe( 2 );

            // The registration survived the reconnection: no re-registering needed.
            FakeWebSocket.last.emitFrame( { connectionId: 'C2' } );
            expect( t.connected ).toEqual( ['C1', 'C2'] );
        } );

        it( 'reports no error when the close was clean', () => {
            const c = connected();
            const t = record( c, 'OD' );
            FakeWebSocket.last.emitClose( true, '' );
            expect( t.closed ).toEqual( [undefined] );
        } );

        it( 'backs off, doubling and capped', () => {
            connected();
            // Every retry now fails at construction, so nothing but the delay advances - and a failed
            // construction reschedules, which is exactly the sequence under test.
            FakeWebSocket.last.emitClose();
            FakeWebSocket.throwOnConstruct = true;

            let attempts = FakeWebSocket.attempts;
            for ( const delay of [1000, 2000, 4000, 8000, 16000, 30000, 30000] ) {
                jest.advanceTimersByTime( delay - 1 );
                expect( FakeWebSocket.attempts ).toBe( attempts );
                jest.advanceTimersByTime( 1 );
                expect( FakeWebSocket.attempts ).toBe( ++attempts );
            }

            // And it does connect once the constructor works again.
            FakeWebSocket.throwOnConstruct = false;
            jest.advanceTimersByTime( 30000 );
            expect( FakeWebSocket.instances.length ).toBe( 2 );
        } );

        it( 'resets the backoff only once a negotiation completed', () => {
            const c = connected();

            // First loss: reconnects after the minimum.
            FakeWebSocket.last.emitClose();
            jest.advanceTimersByTime( 1000 );
            expect( FakeWebSocket.instances.length ).toBe( 2 );

            // Socket accepted but never negotiated, then lost again: the delay must have doubled.
            FakeWebSocket.last.emitClose();
            jest.advanceTimersByTime( 1999 );
            expect( FakeWebSocket.instances.length ).toBe( 2 );
            jest.advanceTimersByTime( 1 );
            expect( FakeWebSocket.instances.length ).toBe( 3 );

            // Now negotiate, lose again: back to the minimum.
            FakeWebSocket.last.emitFrame( { connectionId: 'C2' } );
            FakeWebSocket.last.emitClose();
            jest.advanceTimersByTime( 1000 );
            expect( FakeWebSocket.instances.length ).toBe( 4 );
        } );

        it( 'ignores a socket that has been replaced', () => {
            const c = connected();
            const t = record( c, 'OD' );
            const stale = FakeWebSocket.last;

            stale.emitClose();
            jest.advanceTimersByTime( 1000 );
            expect( FakeWebSocket.instances.length ).toBe( 2 );
            t.closed.length = 0;

            // Everything the old socket says now is late and must be dropped - including its close,
            // which would otherwise schedule a second reconnection.
            stale.emitFrame( { topic: 'OD', message: 'late' } );
            stale.emitClose();
            stale.emitError();
            jest.advanceTimersByTime( 60000 );

            expect( t.messages ).toEqual( [] );
            expect( t.closed ).toEqual( [] );
            expect( FakeWebSocket.instances.length ).toBe( 2 );
        } );
    } );

    describe( 'stopAsync', () => {

        beforeEach( () => jest.useFakeTimers() );
        afterEach( () => jest.useRealTimers() );

        it( 'closes without reporting a loss and does not reconnect', async () => {
            const c = connected();
            const t = record( c, 'OD' );
            const socket = FakeWebSocket.last;

            await c.stopAsync();

            expect( socket.closeCalled ).toBe( true );
            // A deliberate stop is not a connection loss.
            expect( t.closed ).toEqual( [] );
            expect( c.connectionId ).toBeUndefined();

            jest.advanceTimersByTime( 60000 );
            expect( FakeWebSocket.instances.length ).toBe( 1 );
        } );

        it( 'cancels a pending reconnection', async () => {
            const c = connected();
            FakeWebSocket.last.emitClose();
            await c.stopAsync();

            jest.advanceTimersByTime( 60000 );
            expect( FakeWebSocket.instances.length ).toBe( 1 );
        } );

        it( 'can be restarted', () => {
            const c = connected();
            void c.stopAsync();
            c.start();
            expect( FakeWebSocket.instances.length ).toBe( 2 );
        } );
    } );
} );

package it.freeextend.display

import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.InputStream
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.CountDownLatch
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit

/** Prova StreamClient contro un finto host sul PC (ServerSocket su loopback), nel protocollo reale. */
class StreamClientTest {

    private val paramSets = byteArrayOf(0, 0, 0, 1, 0x67, 0xAA.toByte(), 0, 0, 0, 1, 0x68, 0xBB.toByte())
    private val hello = Hello(2304, 1440, 60, 1)
    private var server: ServerSocket? = null
    private var client: StreamClient? = null

    @After
    fun tearDown() {
        client?.stop()
        server?.close()
    }

    private class Events : StreamClient.Listener {
        val configs = LinkedBlockingQueue<Config>()
        val videos = LinkedBlockingQueue<Triple<ByteArray, Boolean, Long>>()
        val disconnected = CountDownLatch(1)
        override fun onConfig(width: Int, height: Int, fps: Int, paramSets: ByteArray) {
            configs.put(Config(width, height, fps, paramSets))
        }
        override fun onVideo(annexB: ByteArray, keyframe: Boolean, timestampUs: Long) {
            videos.put(Triple(annexB, keyframe, timestampUs))
        }
        override fun onDisconnected() { disconnected.countDown() }
    }

    private fun readMagic(s: Socket) {
        val b = ByteArray(4)
        var got = 0
        val i: InputStream = s.getInputStream()
        while (got < 4) {
            val n = i.read(b, got, 4 - got)
            if (n < 0) error("EOF nel magic")
            got += n
        }
        assertArrayEquals(MessageCodec.MAGIC, b)
    }

    /** Fa da host: accetta, legge magic + Hello, risponde con magic + Config. */
    private fun acceptAndHandshake(srv: ServerSocket): Socket {
        val s = srv.accept()
        s.soTimeout = 5000
        readMagic(s)
        val m = MessageCodec.read(s.getInputStream())!!
        assertEquals(MessageType.HELLO, m.type)
        assertEquals(hello, HelloPayload.decode(m.payload))
        s.getOutputStream().write(MessageCodec.MAGIC)
        MessageCodec.write(s.getOutputStream(), Message(MessageType.CONFIG, 0, 0L, ConfigPayload.encode(2304, 1440, 60, paramSets)))
        s.getOutputStream().flush()
        return s
    }

    private fun startClient(port: Int, events: Events) {
        client = StreamClient("127.0.0.1", port, events).also { it.start(hello) }
    }

    @Test
    fun receives_config_and_video() {
        val srv = ServerSocket(0).also { it.soTimeout = 5000; server = it }
        val events = Events()
        startClient(srv.localPort, events)

        val host = acceptAndHandshake(srv)
        val video = byteArrayOf(0, 0, 0, 1, 0x65, 1, 2, 3)
        MessageCodec.write(host.getOutputStream(), Message(MessageType.VIDEO, 1, 777L, video))
        host.getOutputStream().flush()

        val cfg = events.configs.poll(5, TimeUnit.SECONDS)!!
        assertEquals(2304, cfg.width)
        assertEquals(1440, cfg.height)
        assertEquals(60, cfg.fps)
        assertArrayEquals(paramSets, cfg.paramSets)

        val v = events.videos.poll(5, TimeUnit.SECONDS)!!
        assertArrayEquals(video, v.first)
        assertTrue(v.second)
        assertEquals(777L, v.third)
    }

    @Test
    fun answers_ping_with_pong_repeating_the_timestamp() {
        val srv = ServerSocket(0).also { it.soTimeout = 5000; server = it }
        startClient(srv.localPort, Events())

        val host = acceptAndHandshake(srv)
        MessageCodec.write(host.getOutputStream(), Message(MessageType.PING, 0, 99L, ByteArray(4096)))
        host.getOutputStream().flush()

        val pong = MessageCodec.read(host.getInputStream())!!
        assertEquals(MessageType.PONG, pong.type)
        assertEquals(99L, pong.timestampUs)
        assertEquals(0, pong.payload.size)
    }

    @Test
    fun send_writes_a_message_to_the_host() {
        val srv = ServerSocket(0).also { it.soTimeout = 5000; server = it }
        val events = Events()
        startClient(srv.localPort, events)

        val host = acceptAndHandshake(srv)
        events.configs.poll(5, TimeUnit.SECONDS)!!
        client!!.send(Message(MessageType.RENDERED, 0, 5L, ByteArray(0)))

        val m = MessageCodec.read(host.getInputStream())!!
        assertEquals(MessageType.RENDERED, m.type)
        assertEquals(5L, m.timestampUs)
    }

    @Test
    fun retries_after_a_bad_magic_and_connects_when_the_host_is_right() {
        val srv = ServerSocket(0).also { it.soTimeout = 5000; server = it }
        val events = Events()
        startClient(srv.localPort, events)

        val s = srv.accept()
        s.soTimeout = 5000
        readMagic(s)
        MessageCodec.read(s.getInputStream())
        s.getOutputStream().write(byteArrayOf('X'.code.toByte(), 'X'.code.toByte(), 'X'.code.toByte(), 'X'.code.toByte()))
        s.getOutputStream().flush()
        s.close()

        // il client non si arrende: riprova e questa volta l'host e quello giusto
        acceptAndHandshake(srv)
        assertTrue(events.configs.poll(5, TimeUnit.SECONDS) != null)
    }

    @Test
    fun retries_when_the_tunnel_accepts_but_the_host_is_not_there() {
        // adb reverse accetta la connessione sul tablet anche se sul PC non c'e nessuno, poi la chiude subito:
        // il client deve riprovare finche l'host arriva
        val srv = ServerSocket(0).also { it.soTimeout = 5000; server = it }
        val events = Events()
        startClient(srv.localPort, events)

        srv.accept().close()                              // chiusa subito, senza handshake
        srv.accept().close()
        acceptAndHandshake(srv)

        assertTrue(events.configs.poll(5, TimeUnit.SECONDS) != null)
    }

    @Test
    fun reconnects_after_the_session_ends() {
        val srv = ServerSocket(0).also { it.soTimeout = 5000; server = it }
        val events = Events()
        startClient(srv.localPort, events)

        val first = acceptAndHandshake(srv)
        assertTrue(events.configs.poll(5, TimeUnit.SECONDS) != null)
        first.close()                                     // il cavo viene staccato / l'host si ferma
        assertTrue(events.disconnected.await(5, TimeUnit.SECONDS))

        acceptAndHandshake(srv)                           // il client si ripresenta da solo
        assertTrue(events.configs.poll(5, TimeUnit.SECONDS) != null)
    }

    @Test
    fun oversized_payload_from_the_host_disconnects_without_crashing() {
        val srv = ServerSocket(0).also { it.soTimeout = 5000; server = it }
        val events = Events()
        startClient(srv.localPort, events)

        val host = acceptAndHandshake(srv)
        events.configs.poll(5, TimeUnit.SECONDS)!!
        host.getOutputStream().write(byteArrayOf(3, 0, 0, 0, 0x7F, 0xFF.toByte(), 0xFF.toByte(), 0xFF.toByte(), 0, 0, 0, 0, 0, 0, 0, 0))
        host.getOutputStream().flush()

        assertTrue(events.disconnected.await(5, TimeUnit.SECONDS))
    }

    @Test
    fun keeps_retrying_until_the_host_is_up() {
        // il client parte prima dell'host (l'app puo essere aperta prima di avviare il programma sul PC)
        val probe = ServerSocket(0)
        val port = probe.localPort
        probe.close()
        val events = Events()
        startClient(port, events)
        Thread.sleep(1200)                                // il client prova e riprova senza arrendersi

        val srv = ServerSocket(port).also { it.soTimeout = 5000; server = it }
        acceptAndHandshake(srv)

        assertTrue(events.configs.poll(5, TimeUnit.SECONDS) != null)
    }

    @Test
    fun a_silent_connection_is_declared_dead_and_reopened() {
        // l'host smette di mandare qualunque cosa (cavo staccato a meta, PC in blocco): dopo il timeout il client riparte da capo
        val srv = ServerSocket(0).also { it.soTimeout = 5000; server = it }
        val events = Events()
        client = StreamClient("127.0.0.1", srv.localPort, events, silenceTimeoutMs = 300).also { it.start(hello) }

        val first = acceptAndHandshake(srv)                        // Config, poi silenzio assoluto
        assertTrue(events.configs.poll(5, TimeUnit.SECONDS) != null)
        assertTrue(events.disconnected.await(5, TimeUnit.SECONDS))

        acceptAndHandshake(srv)                                    // il client si e ripresentato da solo
        assertTrue(events.configs.poll(5, TimeUnit.SECONDS) != null)
        first.close()
    }

    @Test
    fun traffic_keeps_the_connection_alive_beyond_the_silence_timeout() {
        val srv = ServerSocket(0).also { it.soTimeout = 5000; server = it }
        val events = Events()
        client = StreamClient("127.0.0.1", srv.localPort, events, silenceTimeoutMs = 300).also { it.start(hello) }

        val host = acceptAndHandshake(srv)
        assertTrue(events.configs.poll(5, TimeUnit.SECONDS) != null)
        repeat(6) {                                                // un Ping ogni 100 ms per 600 ms: oltre il timeout, ma mai muto
            MessageCodec.write(host.getOutputStream(), Message(MessageType.PING, 0, it.toLong(), ByteArray(0)))
            host.getOutputStream().flush()
            Thread.sleep(100)
        }

        assertEquals(1L, events.disconnected.count)                // ancora connesso: nessuna disconnessione
    }

    @Test
    fun a_throwing_listener_ends_the_session_but_does_not_kill_the_app() {
        // Un'eccezione (RuntimeException) in un callback, per esempio un decoder che si rifiuta di lavorare, non deve far crashare il
        // processo: la sessione finisce e il client si ripresenta da solo.
        val uncaught = LinkedBlockingQueue<Throwable>()
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { _, e -> uncaught.put(e) }
        try {
            val srv = ServerSocket(0).also { it.soTimeout = 5000; server = it }
            val events = object : StreamClient.Listener {
                val configs = LinkedBlockingQueue<Config>()
                val disconnected = CountDownLatch(1)
                override fun onConfig(width: Int, height: Int, fps: Int, paramSets: ByteArray) { configs.put(Config(width, height, fps, paramSets)) }
                override fun onVideo(annexB: ByteArray, keyframe: Boolean, timestampUs: Long) { throw IllegalStateException("decoder rotto") }
                override fun onDisconnected() { disconnected.countDown() }
            }
            client = StreamClient("127.0.0.1", srv.localPort, events).also { it.start(hello) }

            val host = acceptAndHandshake(srv)
            assertTrue(events.configs.poll(5, TimeUnit.SECONDS) != null)
            MessageCodec.write(host.getOutputStream(), Message(MessageType.VIDEO, 1, 1L, byteArrayOf(0, 0, 0, 1, 0x65)))
            host.getOutputStream().flush()

            assertTrue(events.disconnected.await(5, TimeUnit.SECONDS))     // sessione chiusa, non processo morto
            acceptAndHandshake(srv)                                          // e il client si ripresenta
            assertTrue(events.configs.poll(5, TimeUnit.SECONDS) != null)
            assertTrue("eccezione non gestita sul thread del client: ${uncaught.peek()}", uncaught.isEmpty())
        } finally {
            Thread.setDefaultUncaughtExceptionHandler(previous)
        }
    }
}

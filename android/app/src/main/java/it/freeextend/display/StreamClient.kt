package it.freeextend.display

import android.util.Log
import java.io.BufferedInputStream
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.InetSocketAddress
import java.net.Socket

/**
 * Client del flusso: si connette all'host (127.0.0.1, il cavo arriva li con `adb reverse`), fa l'handshake (magic "FXD1" +
 * Hello, poi magic + Config dall'host) e legge i messaggi. Risponde da solo a ogni PING con un PONG che ripete il timestamp.
 * Se l'host non c'e ancora riprova ogni 500 ms (l'app puo essere aperta prima del programma sul PC).
 * I callback arrivano dal thread di lettura.
 */
class StreamClient(
    private val host: String = "127.0.0.1",
    private val port: Int = DEFAULT_PORT,
    private val listener: Listener,
    /** Se dall'host non arriva nulla per questo tempo la connessione e considerata morta e si riparte (l'host manda almeno un Ping al secondo). */
    private val silenceTimeoutMs: Int = DEFAULT_SILENCE_TIMEOUT_MS,
) {
    interface Listener {
        fun onConfig(width: Int, height: Int, fps: Int, paramSets: ByteArray)
        fun onVideo(annexB: ByteArray, keyframe: Boolean, timestampUs: Long)
        fun onDisconnected()
    }

    @Volatile private var running = false
    @Volatile private var socket: Socket? = null
    @Volatile private var out: OutputStream? = null
    private val writeLock = Any()
    private var thread: Thread? = null

    fun start(hello: Hello) {
        running = true
        thread = Thread({ run(hello) }, "stream-client").also { it.start() }
    }

    /** Invia un messaggio (da qualunque thread). Se la connessione e caduta non fa nulla. */
    fun send(message: Message) {
        try {
            synchronized(writeLock) {
                val o = out ?: return
                MessageCodec.write(o, message)
                o.flush()
            }
        } catch (e: IOException) {
            Log.w(TAG, "client: sessione chiusa: ${e.message}")
            // la lettura se ne accorge e chiude la sessione
        }
    }

    fun stop() {
        running = false
        try { socket?.close() } catch (_: IOException) { }
        thread?.interrupt()
    }

    /**
     * Ciclo di vita: connetti -> handshake -> leggi, e da capo finche [stop] non viene chiamato. Con `adb reverse` la connessione
     * sul tablet riesce subito anche se sul PC non c'e ancora nessuno (poi viene chiusa), quindi "connesso" vale solo dopo
     * l'handshake: una sessione che cade prima di quel punto si riprova in silenzio; una che cade dopo avvisa il listener.
     */
    private fun run(hello: Hello) {
        while (running) {
            var handshakeDone = false
            try {
                val s = connectWithRetry() ?: return
                socket = s
                s.tcpNoDelay = true
                s.soTimeout = silenceTimeoutMs        // lettura senza dati oltre questo tempo = connessione morta (SocketTimeoutException)
                val input = BufferedInputStream(s.getInputStream(), 64 * 1024)
                synchronized(writeLock) {
                    val o = s.getOutputStream()
                    o.write(MessageCodec.MAGIC)
                    MessageCodec.write(o, Message(MessageType.HELLO, 0, 0L, HelloPayload.encode(hello.width, hello.height, hello.refreshHz, hello.codecMask)))
                    o.flush()
                    out = o
                }
                expectMagic(input)
                handshakeDone = true
                Log.i(TAG, "client: handshake completato con $host:$port")
                readLoop(input)
            } catch (e: IOException) {
                // connessione chiusa, troncata o con dati non conformi (ProtocolException e un IOException)
                if (handshakeDone) Log.w(TAG, "client: sessione chiusa: ${e.message}")
            } catch (e: RuntimeException) {
                // Un'eccezione in un callback del listener (decoder rotto, buffer troppo piccolo...) chiude la sessione, non il processo:
                // il client si ripresenta e l'host rimanda Config e un keyframe.
                Log.e(TAG, "client: errore in un callback, sessione chiusa: ${e.javaClass.simpleName}: ${e.message}")
            } finally {
                out = null
                try { socket?.close() } catch (_: IOException) { }
                socket = null
                if (running && handshakeDone) listener.onDisconnected()
            }
            if (running) {
                try { Thread.sleep(RETRY_DELAY_MS) } catch (_: InterruptedException) { return }
            }
        }
    }

    private fun connectWithRetry(): Socket? {
        while (running) {
            val s = Socket()
            try {
                s.connect(InetSocketAddress(host, port), 1000)
                return s
            } catch (_: IOException) {
                try { s.close() } catch (_: IOException) { }
                try { Thread.sleep(RETRY_DELAY_MS) } catch (_: InterruptedException) { return null }
            }
        }
        return null
    }

    private fun expectMagic(input: InputStream) {
        val magic = ByteArray(MessageCodec.MAGIC.size)
        var got = 0
        while (got < magic.size) {
            val n = input.read(magic, got, magic.size - got)
            if (n < 0) throw ProtocolException("connessione chiusa durante l'handshake")
            got += n
        }
        if (!magic.contentEquals(MessageCodec.MAGIC)) throw ProtocolException("magic dell'host non valido")
    }

    private fun readLoop(input: InputStream) {
        while (running) {
            val m = MessageCodec.read(input) ?: return
            when (m.type) {
                MessageType.CONFIG -> {
                    val c = ConfigPayload.decode(m.payload)
                    listener.onConfig(c.width, c.height, c.fps, c.paramSets)
                }
                MessageType.VIDEO -> listener.onVideo(m.payload, m.isKeyframe, m.timestampUs)
                MessageType.PING -> send(Message(MessageType.PONG, 0, m.timestampUs, ByteArray(0)))
                // altri tipi dall'host: ignorati
            }
        }
    }

    companion object {
        const val DEFAULT_PORT = 27183
        const val DEFAULT_SILENCE_TIMEOUT_MS = 3000
        private const val RETRY_DELAY_MS = 500L
        private const val TAG = "FreeExtend"
    }
}

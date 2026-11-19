package it.freeextend.display

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.InputStream

class MessageCodecTest {

    // I test JVM girano con la cartella del modulo (android/app) come directory di lavoro.
    private fun golden(name: String): ByteArray = File("../../protocol/golden/$name").readBytes()

    private fun hex(s: String): ByteArray =
        s.trim().split(Regex("\\s+")).map { it.toInt(16).toByte() }.toByteArray()

    private fun serialize(m: Message): ByteArray {
        val out = ByteArrayOutputStream()
        MessageCodec.write(out, m)
        return out.toByteArray()
    }

    /** InputStream che restituisce un solo byte per read(): simula le letture parziali di TCP. */
    private class OneByteStream(private val data: ByteArray) : InputStream() {
        private var pos = 0
        override fun read(): Int = if (pos < data.size) data[pos++].toInt() and 0xFF else -1
        override fun read(b: ByteArray, off: Int, len: Int): Int {
            if (len == 0) return 0
            val v = read()
            if (v < 0) return -1
            b[off] = v.toByte()
            return 1
        }
    }

    @Test
    fun hello_matches_golden_bytes() {
        val msg = Message(MessageType.HELLO, 0, 0L, HelloPayload.encode(2304, 1440, 60, 1))

        val bytes = serialize(msg)

        assertArrayEquals(hex("01 00 00 00 00 00 00 07 00 00 00 00 00 00 00 00 09 00 05 A0 00 3C 01"), bytes)
        assertArrayEquals(golden("hello.bin"), bytes)
    }

    @Test
    fun video_key_matches_golden_bytes() {
        val payload = hex("00 00 00 01 65 AA")
        val msg = Message(MessageType.VIDEO, 1, 1_234_567L, payload)

        assertArrayEquals(golden("video_key.bin"), serialize(msg))
    }

    @Test
    fun reads_golden_video_key() {
        val msg = MessageCodec.read(ByteArrayInputStream(golden("video_key.bin")))

        assertNotNull(msg)
        assertEquals(MessageType.VIDEO, msg!!.type)
        assertEquals(1, msg.flags)
        assertEquals(1_234_567L, msg.timestampUs)
        assertArrayEquals(hex("00 00 00 01 65 AA"), msg.payload)
    }

    @Test
    fun read_returns_null_on_clean_eof() {
        assertNull(MessageCodec.read(ByteArrayInputStream(ByteArray(0))))
    }

    @Test
    fun read_throws_on_truncated_header() {
        val bytes = serialize(Message(MessageType.PING, 0, 1L, ByteArray(0))).copyOf(10)
        assertThrows(ProtocolException::class.java) { MessageCodec.read(ByteArrayInputStream(bytes)) }
    }

    @Test
    fun read_throws_on_truncated_payload() {
        val bytes = serialize(Message(MessageType.VIDEO, 0, 1L, byteArrayOf(1, 2, 3, 4))).copyOf(16 + 2)
        assertThrows(ProtocolException::class.java) { MessageCodec.read(ByteArrayInputStream(bytes)) }
    }

    @Test
    fun read_throws_on_unknown_type() {
        val bytes = hex("63 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00")
        assertThrows(ProtocolException::class.java) { MessageCodec.read(ByteArrayInputStream(bytes)) }
    }

    @Test
    fun read_throws_on_payload_over_max() {
        // lunghezza dichiarata = MAX_PAYLOAD + 1 = 0x00800001; nel flusso non c'e altro
        val bytes = hex("03 00 00 00 00 80 00 01 00 00 00 00 00 00 00 00")
        val ex = assertThrows(ProtocolException::class.java) { MessageCodec.read(ByteArrayInputStream(bytes)) }
        assertTrue(ex.message!!.contains("payload", ignoreCase = true))
    }

    @Test
    fun read_handles_partial_reads() {
        val payload = byteArrayOf(9, 8, 7, 6, 5)
        val bytes = serialize(Message(MessageType.VIDEO, 1, 42L, payload))

        val msg = MessageCodec.read(OneByteStream(bytes))

        assertNotNull(msg)
        assertEquals(42L, msg!!.timestampUs)
        assertArrayEquals(payload, msg.payload)
    }

    @Test
    fun write_throws_on_payload_over_max() {
        val tooBig = Message(MessageType.VIDEO, 0, 0L, ByteArray(MessageCodec.MAX_PAYLOAD + 1))
        assertThrows(ProtocolException::class.java) { MessageCodec.write(ByteArrayOutputStream(), tooBig) }
    }

    @Test
    fun magic_is_FXD1() {
        assertArrayEquals(byteArrayOf(0x46, 0x58, 0x44, 0x31), MessageCodec.MAGIC)
    }

    @Test
    fun hello_payload_roundtrips() {
        val h = HelloPayload.decode(HelloPayload.encode(2304, 1440, 90, 1))
        assertEquals(Hello(2304, 1440, 90, 1), h)
    }

    @Test
    fun hello_payload_throws_when_too_short() {
        assertThrows(ProtocolException::class.java) { HelloPayload.decode(byteArrayOf(1, 2, 3)) }
    }

    @Test
    fun config_payload_roundtrips_with_param_sets() {
        val paramSets = hex("00 00 00 01 67 AA 00 00 00 01 68 BB")

        val encoded = ConfigPayload.encode(2304, 1440, 60, paramSets)
        assertArrayEquals(hex("09 00 05 A0 00 3C"), encoded.copyOf(6))

        val c = ConfigPayload.decode(encoded)
        assertEquals(2304, c.width)
        assertEquals(1440, c.height)
        assertEquals(60, c.fps)
        assertArrayEquals(paramSets, c.paramSets)
    }

    @Test
    fun config_payload_throws_when_too_short() {
        assertThrows(ProtocolException::class.java) { ConfigPayload.decode(byteArrayOf(1, 2, 3, 4, 5)) }
    }
}

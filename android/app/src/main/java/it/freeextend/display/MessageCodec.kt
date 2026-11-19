package it.freeextend.display

import java.io.InputStream
import java.io.OutputStream
import java.nio.ByteBuffer

/**
 * Serializzazione big-endian. Intestazione di 16 byte:
 * `u8 type | u8 flags | u16 0 | u32 payloadLength | u64 timestampUs`, poi il payload.
 */
object MessageCodec {
    const val HEADER_SIZE = 16
    const val MAX_PAYLOAD = 8 * 1024 * 1024

    /** Primi 4 byte di ogni connessione, inviati da entrambi i lati: ASCII "FXD1". */
    val MAGIC: ByteArray = byteArrayOf(0x46, 0x58, 0x44, 0x31)

    fun write(out: OutputStream, m: Message) {
        if (m.payload.size > MAX_PAYLOAD) {
            throw ProtocolException("payload di ${m.payload.size} byte oltre il massimo di $MAX_PAYLOAD")
        }
        val header = ByteBuffer.allocate(HEADER_SIZE)
        header.put(m.type.toByte())
        header.put(m.flags.toByte())
        header.putShort(0)
        header.putInt(m.payload.size)
        header.putLong(m.timestampUs)
        out.write(header.array())
        out.write(m.payload)
    }

    /**
     * Legge un messaggio. Restituisce null se il flusso finisce esattamente sul confine di un messaggio.
     * Lancia [ProtocolException] se e troncato, il tipo e sconosciuto o il payload supera [MAX_PAYLOAD]
     * (controllato prima di allocare).
     */
    fun read(input: InputStream): Message? {
        val header = ByteArray(HEADER_SIZE)
        val got = readFully(input, header)
        if (got == 0) return null
        if (got < HEADER_SIZE) throw ProtocolException("intestazione troncata ($got di $HEADER_SIZE byte)")

        val buf = ByteBuffer.wrap(header)
        val type = buf.get().toInt() and 0xFF
        val flags = buf.get().toInt() and 0xFF
        buf.getShort() // riservato
        val length = buf.getInt().toLong() and 0xFFFFFFFFL
        val timestampUs = buf.getLong()

        if (type < MessageType.HELLO || type > MessageType.REQUEST_KEYFRAME) {
            throw ProtocolException("tipo di messaggio sconosciuto: $type")
        }
        if (length > MAX_PAYLOAD) {
            throw ProtocolException("payload dichiarato di $length byte oltre il massimo di $MAX_PAYLOAD")
        }

        val payload = ByteArray(length.toInt())
        val payloadGot = readFully(input, payload)
        if (payloadGot < payload.size) {
            throw ProtocolException("payload troncato ($payloadGot di ${payload.size} byte)")
        }
        return Message(type, flags, timestampUs, payload)
    }

    /** Legge fino a riempire il buffer (TCP restituisce letture parziali); meno byte solo a fine flusso. */
    private fun readFully(input: InputStream, buffer: ByteArray): Int {
        var total = 0
        while (total < buffer.size) {
            val n = input.read(buffer, total, buffer.size - total)
            if (n < 0) break
            total += n
        }
        return total
    }
}

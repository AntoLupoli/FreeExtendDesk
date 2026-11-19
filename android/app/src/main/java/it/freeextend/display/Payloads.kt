package it.freeextend.display

import java.nio.ByteBuffer

/** Contenuto di HELLO (tablet -> host). [codecMask] bit 0 = H.264. */
data class Hello(val width: Int, val height: Int, val refreshHz: Int, val codecMask: Int)

object HelloPayload {
    const val SIZE = 7

    fun encode(width: Int, height: Int, refreshHz: Int, codecMask: Int): ByteArray {
        val b = ByteBuffer.allocate(SIZE)
        b.putShort(width.toShort()).putShort(height.toShort()).putShort(refreshHz.toShort())
        b.put(codecMask.toByte())
        return b.array()
    }

    fun decode(payload: ByteArray): Hello {
        if (payload.size < SIZE) throw ProtocolException("Hello troppo corto (${payload.size} byte, servono $SIZE)")
        val b = ByteBuffer.wrap(payload)
        return Hello(
            b.getShort().toInt() and 0xFFFF,
            b.getShort().toInt() and 0xFFFF,
            b.getShort().toInt() and 0xFFFF,
            b.get().toInt() and 0xFF,
        )
    }
}

/** Contenuto di CONFIG (host -> tablet): dimensioni, fps e SPS/PPS in Annex-B. */
class Config(val width: Int, val height: Int, val fps: Int, val paramSets: ByteArray)

object ConfigPayload {
    const val FIXED_SIZE = 6

    fun encode(width: Int, height: Int, fps: Int, paramSets: ByteArray): ByteArray {
        val b = ByteBuffer.allocate(FIXED_SIZE + paramSets.size)
        b.putShort(width.toShort()).putShort(height.toShort()).putShort(fps.toShort())
        b.put(paramSets)
        return b.array()
    }

    fun decode(payload: ByteArray): Config {
        if (payload.size < FIXED_SIZE) {
            throw ProtocolException("Config troppo corto (${payload.size} byte, servono almeno $FIXED_SIZE)")
        }
        val b = ByteBuffer.wrap(payload)
        val w = b.getShort().toInt() and 0xFFFF
        val h = b.getShort().toInt() and 0xFFFF
        val fps = b.getShort().toInt() and 0xFFFF
        return Config(w, h, fps, payload.copyOfRange(FIXED_SIZE, payload.size))
    }
}

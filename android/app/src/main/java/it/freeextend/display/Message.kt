package it.freeextend.display

import java.io.IOException

/** Dati non conformi al protocollo: la connessione va chiusa. */
class ProtocolException(message: String) : IOException(message)

/** Tipi di messaggio (vedi protocol/golden/README.md). */
object MessageType {
    const val HELLO = 1
    const val CONFIG = 2
    const val VIDEO = 3
    const val PING = 4
    const val PONG = 5
    const val RENDERED = 6
    const val REQUEST_KEYFRAME = 7
}

/**
 * Un messaggio del flusso. [flags] bit 0 = keyframe (solo VIDEO). [timestampUs] e l'istante di cattura
 * sull'orologio dell'host; PING/PONG/RENDERED lo ripetono.
 */
class Message(val type: Int, val flags: Int, val timestampUs: Long, val payload: ByteArray) {
    val isKeyframe: Boolean get() = flags and 1 != 0

    override fun equals(other: Any?): Boolean =
        other is Message && type == other.type && flags == other.flags &&
            timestampUs == other.timestampUs && payload.contentEquals(other.payload)

    override fun hashCode(): Int =
        ((type * 31 + flags) * 31 + timestampUs.hashCode()) * 31 + payload.contentHashCode()

    override fun toString(): String = "Message(type=$type, flags=$flags, ts=$timestampUs, payload=${payload.size}B)"
}

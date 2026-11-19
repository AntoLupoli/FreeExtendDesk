package it.freeextend.display

/**
 * Regola di alimentazione del decoder: all'inizio e dopo ogni frame perso si accettano solo keyframe. Un frame non-keyframe
 * senza il suo riferimento darebbe un'immagine corrotta. Non dipende da MediaCodec, quindi si prova sulla JVM.
 */
class KeyframeGate {
    @Volatile private var waiting = true

    /** Vero se il frame va dato al decoder. Un keyframe e sempre accettato e riapre il cancello. */
    fun accepts(keyframe: Boolean): Boolean {
        if (keyframe) {
            waiting = false
            return true
        }
        return !waiting
    }

    /** Un frame e stato perso (nessun buffer libero, frame troppo grande, errore del codec): servira un nuovo keyframe. */
    fun markBroken() {
        waiting = true
    }

    companion object {
        /** Il frame entra nel buffer di ingresso del codec? (sennò `put` lancia BufferOverflowException.) */
        fun fits(frameSize: Int, bufferCapacity: Int): Boolean = frameSize <= bufferCapacity
    }
}

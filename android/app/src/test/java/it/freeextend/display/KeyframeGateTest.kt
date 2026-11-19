package it.freeextend.display

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Regola di alimentazione del decoder: dopo un frame perso si riparte solo da un keyframe (un P-frame darebbe immagine corrotta). */
class KeyframeGateTest {

    @Test
    fun starts_closed_and_opens_on_the_first_keyframe() {
        val gate = KeyframeGate()

        assertFalse(gate.accepts(keyframe = false))     // niente riferimento: un delta non si puo decodificare
        assertTrue(gate.accepts(keyframe = true))
        assertTrue(gate.accepts(keyframe = false))      // ora i delta passano
    }

    @Test
    fun a_lost_frame_closes_the_gate_until_the_next_keyframe() {
        val gate = KeyframeGate()
        gate.accepts(keyframe = true)

        gate.markBroken()                               // il decoder non aveva un buffer libero: frame perso

        assertFalse(gate.accepts(keyframe = false))
        assertFalse(gate.accepts(keyframe = false))
        assertTrue(gate.accepts(keyframe = true))
        assertTrue(gate.accepts(keyframe = false))
    }

    @Test
    fun a_keyframe_is_always_accepted_even_when_broken() {
        val gate = KeyframeGate()
        gate.markBroken()
        assertTrue(gate.accepts(keyframe = true))
    }

    @Test
    fun an_oversized_frame_is_rejected_by_the_fit_check() {
        assertTrue(KeyframeGate.fits(frameSize = 1000, bufferCapacity = 1000))
        assertFalse(KeyframeGate.fits(frameSize = 1001, bufferCapacity = 1000))
        assertTrue(KeyframeGate.fits(frameSize = 0, bufferCapacity = 1000))
    }
}

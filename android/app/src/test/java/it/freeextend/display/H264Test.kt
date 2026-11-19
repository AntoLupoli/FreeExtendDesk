package it.freeextend.display

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertNull
import org.junit.Test

class H264Test {

    private fun hex(s: String): ByteArray =
        s.trim().split(Regex("\\s+")).map { it.toInt(16).toByte() }.toByteArray()

    @Test
    fun splits_sps_and_pps_keeping_four_byte_start_codes() {
        val ps = H264.splitParamSets(hex("00 00 00 01 67 AA 00 00 00 01 68 BB"))

        assertArrayEquals(hex("00 00 00 01 67 AA"), ps.sps)
        assertArrayEquals(hex("00 00 00 01 68 BB"), ps.pps)
    }

    @Test
    fun normalizes_three_byte_start_codes_to_four_bytes() {
        val ps = H264.splitParamSets(hex("00 00 01 67 AA 00 00 01 68 BB"))

        assertArrayEquals(hex("00 00 00 01 67 AA"), ps.sps)
        assertArrayEquals(hex("00 00 00 01 68 BB"), ps.pps)
    }

    @Test
    fun ignores_other_nal_units_between_them() {
        // AUD (tipo 9) prima, poi SPS, PPS e un IDR: solo SPS e PPS interessano
        val ps = H264.splitParamSets(hex("00 00 00 01 09 F0 00 00 00 01 67 AA 00 00 00 01 68 BB 00 00 01 65 CC"))

        assertArrayEquals(hex("00 00 00 01 67 AA"), ps.sps)
        assertArrayEquals(hex("00 00 00 01 68 BB"), ps.pps)
    }

    @Test
    fun returns_nulls_for_empty_or_unparsable_input() {
        val empty = H264.splitParamSets(ByteArray(0))
        assertNull(empty.sps)
        assertNull(empty.pps)

        val junk = H264.splitParamSets(hex("01 02 03 04"))
        assertNull(junk.sps)
        assertNull(junk.pps)
    }
}

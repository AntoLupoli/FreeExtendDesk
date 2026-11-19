package it.freeextend.display

/** SPS e PPS estratti da un flusso Annex-B, ciascuno con start code a 4 byte (formato atteso da `csd-0`/`csd-1` di MediaCodec). */
class ParamSets(val sps: ByteArray?, val pps: ByteArray?)

object H264 {
    private const val NAL_SPS = 7
    private const val NAL_PPS = 8
    private val START_CODE = byteArrayOf(0, 0, 0, 1)

    fun splitParamSets(annexB: ByteArray): ParamSets {
        // posizione dei byte "00 00 01" (anche quando sono la coda di uno start code a 4 byte)
        val starts = ArrayList<Int>()
        var i = 0
        while (i + 2 < annexB.size) {
            if (annexB[i] == 0.toByte() && annexB[i + 1] == 0.toByte() && annexB[i + 2] == 1.toByte()) {
                starts.add(i)
                i += 3
            } else {
                i++
            }
        }

        var sps: ByteArray? = null
        var pps: ByteArray? = null
        for (k in starts.indices) {
            val nalStart = starts[k] + 3
            if (nalStart >= annexB.size) continue
            var end = if (k + 1 < starts.size) starts[k + 1] else annexB.size
            if (k + 1 < starts.size) while (end > nalStart && annexB[end - 1] == 0.toByte()) end--   // zero iniziale dello start code a 4 byte
            val type = annexB[nalStart].toInt() and 0x1F
            val nal = START_CODE + annexB.copyOfRange(nalStart, end)
            if (type == NAL_SPS && sps == null) sps = nal
            if (type == NAL_PPS && pps == null) pps = nal
        }
        return ParamSets(sps, pps)
    }
}

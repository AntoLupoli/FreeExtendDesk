package it.freeextend.display

import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaCodecList
import android.media.MediaFormat
import android.os.Handler
import android.os.HandlerThread
import android.os.SystemClock
import android.util.Log
import android.view.Surface
import java.nio.ByteBuffer

/**
 * Decoder H.264 hardware in modalita low-latency, che disegna direttamente sulla [surface].
 * Regole di latenza: nessuna coda lato app. Se il codec non ha un buffer di ingresso libero il frame si scarta, si smette di
 * alimentare il decoder e si chiede un keyframe all'host ([onNeedKeyframe]); i frame non-keyframe si ignorano finche non
 * ne arriva uno (un P-frame dopo un frame perso darebbe immagine corrotta). Ogni frame disegnato viene segnalato con
 * [onRendered] (stesso timestamp di cattura dell'host).
 */
class VideoDecoder(
    private val surface: Surface,
    private val onRendered: (timestampUs: Long) -> Unit,
    private val onNeedKeyframe: () -> Unit,
    /** Il codec ha dato un errore non recuperabile (flusso corrotto, risorse perse): chi usa il decoder deve riavviare la sessione. */
    private val onFatalError: (Exception) -> Unit = {},
) {
    private var codec: MediaCodec? = null
    private var outputThread: Thread? = null
    private var callbackThread: HandlerThread? = null
    @Volatile private var running = false
    private val gate = KeyframeGate()
    private var lastKeyframeRequestMs = 0L

    /** Nome del codec scelto (per verificare che sia hardware). */
    var codecName: String = "-"
        private set

    fun configure(width: Int, height: Int, paramSets: ByteArray) {
        val ps = H264.splitParamSets(paramSets)
        val format = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, width, height).apply {
            setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
            setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, width * height)           // un frame H.264 compresso non supera mai il non compresso
            setInteger(MediaFormat.KEY_PRIORITY, 0)                              // tempo reale
            setInteger(MediaFormat.KEY_OPERATING_RATE, OPERATING_RATE_FPS)       // chiede il clock alto (32767 dava ERROR_INSUFFICIENT_RESOURCE a 2304x1440)
            ps.sps?.let { setByteBuffer("csd-0", ByteBuffer.wrap(it)) }
            ps.pps?.let { setByteBuffer("csd-1", ByteBuffer.wrap(it)) }
        }
        val c = createDecoder(format)
        codecName = c.name
        Log.i(TAG, "decoder scelto: ${c.name} (${width}x$height)")
        logCapabilities(c, width, height)
        try {
            c.configure(format, surface, null, 0)

            val cb = HandlerThread("decoder-callbacks").also { it.start() }
            callbackThread = cb
            c.setOnFrameRenderedListener({ _, presentationTimeUs, _ -> onRendered(presentationTimeUs) }, Handler(cb.looper))

            c.start()
        } catch (e: MediaCodec.CodecException) {
            Log.e(TAG, "codec: errore code=${e.errorCode} diag=${e.diagnosticInfo} recoverable=${e.isRecoverable} transient=${e.isTransient}")
            runCatching { c.release() }
            callbackThread?.quitSafely()
            callbackThread = null
            throw e
        }
        codec = c
        running = true
        gate.markBroken()
        outputThread = Thread(::drainOutput, "decoder-output").also { it.start() }
    }

    private fun logCapabilities(c: MediaCodec, width: Int, height: Int) {
        val caps = c.codecInfo.getCapabilitiesForType(MediaFormat.MIMETYPE_VIDEO_AVC)
        val v = caps.videoCapabilities
        Log.i(TAG, "codec: ${c.name} supporta ${width}x$height = ${v.isSizeSupported(width, height)}; larghezze ${v.supportedWidths}, altezze ${v.supportedHeights}")
        Log.i(TAG, "codec: livelli AVC = " + caps.profileLevels.joinToString { "p${it.profile}/l${it.level}" })
        Log.i(TAG, "codec: low-latency supportato = ${caps.isFeatureSupported(MediaCodecInfo.CodecCapabilities.FEATURE_LowLatency)}")
    }

    /** Preferisce un decoder H.264 hardware; se non ce n'e uno adatto usa quello predefinito del sistema. */
    private fun createDecoder(format: MediaFormat): MediaCodec {
        val candidates = MediaCodecList(MediaCodecList.REGULAR_CODECS).codecInfos.filter { info ->
            !info.isEncoder && info.supportedTypes.any { it.equals(MediaFormat.MIMETYPE_VIDEO_AVC, ignoreCase = true) }
        }
        for (info in candidates) Log.i(TAG, "decoder H.264: ${info.name} hardware=${info.isHardwareAccelerated} software=${info.isSoftwareOnly}")
        val hw = candidates.firstOrNull { info ->
            info.isHardwareAccelerated && !info.isSoftwareOnly && runCatching {
                info.getCapabilitiesForType(MediaFormat.MIMETYPE_VIDEO_AVC).isFormatSupported(format)
            }.getOrDefault(false)
        }
        return if (hw != null) MediaCodec.createByCodecName(hw.name) else MediaCodec.createDecoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
    }

    /** Offre un frame al decoder (dal thread di rete). */
    fun queue(annexB: ByteArray, keyframe: Boolean, timestampUs: Long) {
        val c = codec ?: return
        if (!gate.accepts(keyframe)) {
            requestKeyframeThrottled()                    // dopo un frame perso un delta non si puo decodificare
            return
        }
        try {
            val i = c.dequeueInputBuffer(INPUT_TIMEOUT_US)
            if (i < 0) {
                gate.markBroken()                         // frame perso: la catena e rotta fino al prossimo keyframe
                requestKeyframeThrottled()
                return
            }
            val buf = c.getInputBuffer(i)!!
            buf.clear()
            if (!KeyframeGate.fits(annexB.size, buf.capacity())) {
                // Frame piu grande del buffer: put() lancerebbe BufferOverflowException e farebbe cadere l'app. Si restituisce il
                // buffer vuoto, si scarta il frame e si chiede un keyframe.
                c.queueInputBuffer(i, 0, 0, 0, 0)
                gate.markBroken()
                Log.w(TAG, "frame di ${annexB.size} byte non entra nel buffer da ${buf.capacity()}: scartato")
                requestKeyframeThrottled()
                return
            }
            buf.put(annexB)
            c.queueInputBuffer(i, 0, annexB.size, timestampUs, 0)
        } catch (e: MediaCodec.CodecException) {
            Log.e(TAG, "codec: errore in ingresso code=${e.errorCode} recoverable=${e.isRecoverable}")
            gate.markBroken()
            onFatalError(e)
        } catch (e: IllegalStateException) {
            Log.w(TAG, "decoder non pronto: ${e.message}")
        }
    }

    private fun requestKeyframeThrottled() {
        val now = SystemClock.elapsedRealtime()
        if (now - lastKeyframeRequestMs >= KEYFRAME_REQUEST_INTERVAL_MS) {
            lastKeyframeRequestMs = now
            onNeedKeyframe()
        }
    }

    private fun drainOutput() {
        val info = MediaCodec.BufferInfo()
        while (running) {
            val c = codec ?: break
            try {
                val i = c.dequeueOutputBuffer(info, OUTPUT_TIMEOUT_US)
                if (i >= 0) c.releaseOutputBuffer(i, true)           // disegna subito: l'ultimo frame vince
            } catch (e: MediaCodec.CodecException) {
                // Prima il thread di uscita moriva in silenzio e lo schermo restava congelato: ora si segnala per riavviare la sessione.
                Log.e(TAG, "codec: errore in uscita code=${e.errorCode} recoverable=${e.isRecoverable}")
                if (running) onFatalError(e)
                break
            } catch (e: IllegalStateException) {
                break
            }
        }
    }

    fun release() {
        running = false
        try { outputThread?.join(300) } catch (_: InterruptedException) { }
        try { codec?.stop() } catch (_: IllegalStateException) { }
        try { codec?.release() } catch (_: IllegalStateException) { }
        codec = null
        callbackThread?.quitSafely()
        callbackThread = null
    }

    companion object {
        private const val TAG = "FreeExtend"
        private const val OPERATING_RATE_FPS = 120
        private const val INPUT_TIMEOUT_US = 5_000L
        private const val OUTPUT_TIMEOUT_US = 10_000L
        private const val KEYFRAME_REQUEST_INTERVAL_MS = 500L
    }
}

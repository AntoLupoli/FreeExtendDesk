package it.freeextend.display

import android.app.Activity
import android.graphics.Color
import android.os.Bundle
import android.util.Log
import android.view.Gravity
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.Surface
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import android.widget.FrameLayout
import android.widget.TextView

/**
 * Schermata unica: un SurfaceView a tutto schermo dove il decoder disegna il secondo monitor del PC.
 * Resta acceso mentre l'app e aperta. Finche non arriva il primo frame mostra un testo di stato.
 */
class MainActivity : Activity(), SurfaceHolder.Callback, StreamClient.Listener {

    private lateinit var surfaceView: SurfaceView
    private lateinit var status: TextView
    private var surface: Surface? = null
    private var client: StreamClient? = null
    @Volatile private var decoder: VideoDecoder? = null
    @Volatile private var firstFrameShown = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)       // niente spegnimento schermo mentre l'app e aperta
        surfaceView = SurfaceView(this).also { it.holder.addCallback(this) }
        status = TextView(this).apply {
            setTextColor(Color.WHITE)
            textSize = 22f
            gravity = Gravity.CENTER
            text = "FreeExtend\nIn attesa del PC..."
        }
        setContentView(FrameLayout(this).apply {
            setBackgroundColor(Color.BLACK)
            addView(surfaceView, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
            addView(status, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        })

        // Schermo intero immersivo: va fatto dopo setContentView, quando la finestra (DecorView) esiste.
        window.setDecorFitsSystemWindows(false)
        window.insetsController?.let {
            it.hide(WindowInsets.Type.systemBars())
            it.systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
    }

    // --- SurfaceHolder.Callback ---

    override fun surfaceCreated(holder: SurfaceHolder) {
        Log.i(TAG, "surfaceCreated: avvio il client")
        surface = holder.surface
        firstFrameShown = false
        client = StreamClient(listener = this).also { it.start(currentHello()) }
    }

    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) = Unit

    override fun surfaceDestroyed(holder: SurfaceHolder) {
        client?.stop()
        client = null
        decoder?.release()
        decoder = null
        surface = null
    }

    /**
     * Risoluzione reale in orizzontale (2304x1440 sul Tab S10 FE) e refresh MASSIMO che lo schermo sa mostrare (90 Hz sul Tab S10 FE):
     * e l'host a limitarlo (`--fps`, 60 di default) e a scegliere il modo del monitor virtuale.
     */
    private fun currentHello(): Hello {
        val b = windowManager.currentWindowMetrics.bounds
        val w = maxOf(b.width(), b.height())
        val h = minOf(b.width(), b.height())
        val maxHz = display?.supportedModes?.maxOfOrNull { it.refreshRate.toInt() } ?: DEFAULT_REFRESH_HZ
        return Hello(w, h, maxHz, CODEC_H264)
    }

    // --- StreamClient.Listener (chiamati dal thread di rete) ---

    override fun onConfig(width: Int, height: Int, fps: Int, paramSets: ByteArray) {
        val s = surface ?: return
        decoder?.release()
        val d = VideoDecoder(
            surface = s,
            onRendered = { ts ->
                client?.send(Message(MessageType.RENDERED, 0, ts, ByteArray(0)))
                if (!firstFrameShown) {
                    firstFrameShown = true
                    runOnUiThread { status.visibility = TextView.GONE }
                }
            },
            onNeedKeyframe = { client?.send(Message(MessageType.REQUEST_KEYFRAME, 0, 0L, ByteArray(0))) },
            onFatalError = { restartSession("errore del decoder") },
        )
        try {
            d.configure(width, height, paramSets)
        } catch (e: RuntimeException) {
            Log.e(TAG, "decoder non avviato: ${e.message}")
            runOnUiThread { status.text = "Decoder non avviato\n${e.javaClass.simpleName}" }
            return
        }
        decoder = d
        // Chiede al sistema di far girare lo schermo al ritmo del flusso (altrimenti con contenuto a 60 fps puo restare a 90 Hz adattivo).
        s.setFrameRate(fps.toFloat(), Surface.FRAME_RATE_COMPATIBILITY_FIXED_SOURCE)
        runOnUiThread { status.text = "Connesso ${width}x$height@$fps\ndecoder: ${d.codecName}" }
        Log.i(TAG, "config ${width}x$height@$fps, decoder ${d.codecName}")
    }

    /**
     * Il decoder e andato in errore: si chiude la sessione e se ne apre una nuova. L'host vede il tablet ricollegarsi e manda di nuovo
     * Config e un keyframe, quindi lo schermo riparte da solo invece di restare congelato.
     */
    @Synchronized
    private fun restartSession(reason: String) {
        if (surface == null) return                       // l'app e in chiusura
        Log.w(TAG, "riavvio la sessione: $reason")
        client?.stop()
        decoder?.release()
        decoder = null
        firstFrameShown = false
        client = StreamClient(listener = this).also { it.start(currentHello()) }
        runOnUiThread { status.visibility = TextView.VISIBLE; status.text = "FreeExtend\nRiconnessione..." }
    }

    override fun onVideo(annexB: ByteArray, keyframe: Boolean, timestampUs: Long) {
        decoder?.queue(annexB, keyframe, timestampUs)
    }

    override fun onDisconnected() {
        decoder?.release()
        decoder = null
        firstFrameShown = false
        runOnUiThread {
            status.visibility = TextView.VISIBLE
            status.text = "FreeExtend\nIn attesa del PC..."
        }
    }

    companion object {
        private const val TAG = "FreeExtend"
        private const val DEFAULT_REFRESH_HZ = 60
        private const val CODEC_H264 = 1
    }
}

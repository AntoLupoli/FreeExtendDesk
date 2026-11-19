using Vortice.Direct3D11;

namespace FreeExtend.Host.Capture;

public interface IFrameSource : IDisposable
{
    /// <summary>Dispositivo D3D11 sul quale vivono le texture restituite (l'encoder ci lavora sopra).</summary>
    ID3D11Device Device { get; }

    int Width { get; }
    int Height { get; }

    /// <summary>
    /// Attende un nuovo frame per al massimo <paramref name="timeoutMs"/>. Restituisce <c>false</c> se non c'e nulla di nuovo
    /// (desktop fermo, accesso temporaneamente perso, ricreazione in corso): <b>non</b> e un errore.
    /// </summary>
    /// <param name="wanted">Se fornito e restituisce <c>false</c>, il frame NON viene copiato (si evita lavoro GPU inutile per un frame che verra scartato).</param>
    bool TryAcquireFrame(int timeoutMs, out CapturedFrame frame, Func<bool>? wanted = null);
}

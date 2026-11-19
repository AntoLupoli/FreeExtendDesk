using Vortice.Direct3D11;

namespace FreeExtend.Host.Capture;

/// <summary>
/// Un frame catturato. <see cref="Texture"/> e una copia posseduta dalla sorgente, in formato B8G8R8A8,
/// valida fino alla prossima chiamata a <see cref="IFrameSource.TryAcquireFrame"/>.
/// </summary>
public readonly record struct CapturedFrame(ID3D11Texture2D Texture, long CaptureUs);

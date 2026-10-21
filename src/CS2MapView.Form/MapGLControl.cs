using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System.ComponentModel;

namespace CS2MapView.Form;

/// <summary>Native OpenGL map control with reusable path masks disabled.</summary>
/// <remarks>
/// Uses the context/surface lifecycle of SkiaSharp.Views.Desktop.SKGLControl
/// (MIT; see NOTICES), with an explicit context option. Its stock context
/// factory does not expose options. Road clips can produce incorrect cached
/// masks on later frames even when the first frame is correct.
/// </remarks>
internal sealed class MapGLControl : SKGLControl
{
    private GRContext? context;
    private GRBackendRenderTarget? target;
    private SKSurface? surface;
    private SKSizeI size;

    internal GRContext? MapContext => context;

    protected override void OnPaint(PaintEventArgs e)
    {
        if (DesignMode || LicenseManager.UsageMode == LicenseUsageMode.Designtime)
        {
            e.Graphics.Clear(BackColor);
            return;
        }
        if (Width <= 0 || Height <= 0) return;
        MakeCurrent();
        if (context is null)
        {
            using var gl = GRGlInterface.Create();
            context = GRContext.CreateGl(gl, new GRContextOptions { AllowPathMaskCaching = false })
                ?? throw new InvalidOperationException("Unable to create the map OpenGL context.");
        }
        var currentSize = new SKSizeI(Width, Height);
        if (surface is null || target is null || !target.IsValid || currentSize != size)
        {
            ReleaseSurface();
            GL.GetInteger(GetPName.FramebufferBinding, out int framebuffer);
            GL.GetInteger(GetPName.StencilBits, out int stencilBits);
            GL.GetInteger(GetPName.Samples, out int samples);
            samples = Math.Min(samples, context.GetMaxSurfaceSampleCount(SKColorType.Rgba8888));
            var info = new GRGlFramebufferInfo((uint)framebuffer, (uint)SizedInternalFormat.Rgba8);
            target = new GRBackendRenderTarget(Width, Height, samples, stencilBits, info);
            surface = SKSurface.Create(context, target, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
                ?? throw new InvalidOperationException("Unable to create the map OpenGL surface.");
            size = currentSize;
        }
        using (new SKAutoCanvasRestore(surface!.Canvas, true))
            OnPaintSurface(new SKPaintGLSurfaceEventArgs(surface, target!, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888));
        surface.Canvas.Flush();
        SwapBuffers();
    }

    private void ReleaseSurface()
    {
        surface?.Dispose(); surface = null;
        target?.Dispose(); target = null;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ReleaseGraphics();
        base.OnHandleDestroyed(e);
    }

    private void ReleaseGraphics()
    {
        if (context is null) return;
        if (IsHandleCreated && !IsDisposed) MakeCurrent();
        ReleaseSurface();
        context.Dispose(); context = null;
        size = default;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ReleaseGraphics();
        base.Dispose(disposing);
    }
}

using SkiaSharp;

namespace CS2MapView.Drawing;

/// <summary>A recorded native drawing with its geometry already in layer coordinates.</summary>
public sealed class NativePictureDrawCommand : IDrawCommand
{
    private SKPicture? picture;
    public SKRect BoundingRect { get; }
    public NativePictureDrawCommand(SKPicture picture, SKRect bounds)
    {
        this.picture = picture;
        BoundingRect = bounds;
    }
    public void Draw(SKCanvas canvas)
    {
        if (picture != null) canvas.DrawPicture(picture);
    }
    public void Dispose() { picture?.Dispose(); picture = null; }
}

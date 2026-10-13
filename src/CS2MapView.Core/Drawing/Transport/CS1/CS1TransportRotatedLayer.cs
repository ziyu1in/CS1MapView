using CS2MapView.Data;
using CS2MapView.Drawing.Layer;
using SkiaSharp;
using System.Diagnostics.CodeAnalysis;

namespace CS2MapView.Drawing.Transport.CS1;

// RotatedLayer normally assumes commands were built for the current view.
// During an asynchronous navigation rebuild they still carry the previous
// rotation/scale. Remember that geometry transform so the current view moves
// the existing native routes and station markers together with the base map.
internal sealed class CS1TransportRotatedLayer : RotatedLayer
{
    private SKMatrix geometryTransform = SKMatrix.Identity;

    [SetsRequiredMembers]
    internal CS1TransportRotatedLayer(ICS2MapViewRoot root, ReadonlyRect transformedWorldBound,
        IRebuildOnResizeLayerBuilder builder)
        : base(root, ILayer.LayerNameTransportLines, transformedWorldBound, builder) { }

    // Caller owns DrawObjectsLock; commands and their transform are one snapshot.
    internal void SetBuiltGeometryTransform(SKMatrix transform) => geometryTransform = transform;

    protected override void SetMatrix(SKCanvas canvas, ViewContext vc)
        => canvas.SetMatrix(geometryTransform.Invert().PostConcat(vc.ViewTransform));

    protected override IEnumerable<IDrawCommand> GetTargetDrawCommands(SKRect visibleWorldRect,
        SKRect canvasRect, ViewContext vc)
        => DrawCommands.GetOrderedObjects(geometryTransform.MapRect(visibleWorldRect));
}

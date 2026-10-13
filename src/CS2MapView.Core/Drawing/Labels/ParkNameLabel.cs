namespace CS2MapView.Drawing.Labels;

/// <summary>A CS1 park-area name placed by the upstream label arranger.</summary>
internal sealed class ParkNameLabel : AbstractTextLabel
{
    internal override bool Freezed => false;
    internal override bool MayYield => false;
    internal override int YieldPriority => 90;
}

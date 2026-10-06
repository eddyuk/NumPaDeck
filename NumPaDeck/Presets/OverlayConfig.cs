namespace NumPaDeck.Presets;

/// <summary>
/// Settings for the floating numpad overlay (UI/NumpadOverlayForm):
/// shown or hidden, its translucency, and where the user last dragged it.
/// X/Y are null until the overlay has been moved (then it sits at the
/// default bottom-right of the primary work area).
/// </summary>
public class OverlayConfig
{
    public bool Visible { get; set; } = true;

    /// <summary>0.15–1.0, applied as the form's Opacity (translucency).</summary>
    public double Opacity { get; set; } = 0.75;

    public int? X { get; set; }

    public int? Y { get; set; }
}

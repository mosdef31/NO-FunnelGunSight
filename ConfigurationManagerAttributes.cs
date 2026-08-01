using System;

// Passed as a tag on a ConfigDescription to control how BepInEx's
// ConfigurationManager renders a setting.
//
// ConfigurationManager finds this type by *name* through reflection, not by
// assembly reference — that is the documented way to use it, and it means the
// mod does not take a hard dependency on ConfigurationManager being installed.
// If it is absent the tag is simply ignored. Keep the class name and the field
// names exactly as they are; renaming any of them silently stops the hint from
// being read.
//
// Only the members FunnelGunSight actually sets are declared here. The upstream
// class has more; unused ones are omitted rather than carried dead.
#pragma warning disable CS0649 // assigned via object initialisers, never in-class
internal sealed class ConfigurationManagerAttributes
{
    // Higher values sort closer to the top of a section. Without this,
    // ConfigurationManager falls back to alphabetical order, which scatters
    // related settings (e.g. RangeDotSize lands nowhere near ShowRangeDot).
    public int? Order;

    // Hidden unless the user ticks "Advanced settings". Used for knobs that a
    // new player should never need to touch.
    public bool? IsAdvanced;

    // Shown instead of the raw key name.
    public string? DispName;
}
#pragma warning restore CS0649

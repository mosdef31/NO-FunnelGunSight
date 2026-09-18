namespace FunnelGunSight
{
    /// <summary>
    /// SELECTABLE RETICLE SHAPES FOR THE GROUND-ATTACK SIGHT. Owner, 2026-09-17:
    /// "i want to make different selectable shapes or themes for the Ground attack
    /// sight", with four designs supplied as SVG.
    ///
    /// THE FOUR ARE TRANSCRIBED, NOT REINTERPRETED. Each one below carries the
    /// geometry of its own SVG in the file's original 1000x1000 frame, centred on
    /// (500, 500), and <see cref="FunnelRenderer"/> scales that frame to the
    /// pipper's drawn radius. Keeping the original numbers in the comments is what
    /// makes a later "the diamond looks wrong" answerable by measurement rather
    /// than by taste.
    ///
    /// WHY THE FRAME IS NORMALISED PER SHAPE AND NOT GLOBALLY. The designs do not
    /// share a natural size: the diamond's chevrons sit at radius 280 while the
    /// funnel pipper's own ring is radius 20 with a heading stalk running 450 above
    /// it. Scaling all four by one constant would make one of them a dot and
    /// another fill the screen. So each shape declares which of its own features IS
    /// the pipper radius, and everything else follows from that.
    /// </summary>
    public enum GroundSightShape
    {
        /// <summary>
        /// A ring with a dot inside it. THE DEFAULT, AND IT STAYS THE DEFAULT: the
        /// owner chose it on 2026-09-17 after flying a cross version - "make CCIP
        /// ground sight a ring with a dot inside it instead, it seems accurate" -
        /// and adding alternatives is not a reason to overturn that.
        /// </summary>
        RingDot,

        /// <summary>
        /// design1_helo_crosshair. Two vertical rails with a ladder of range ticks
        /// and a broken horizontal bar through the centre. The widest of the four
        /// and the one that reads most like a helicopter sight.
        /// </summary>
        HeloCrosshair,

        /// <summary>
        /// design2_diamond_reticle. Four chevrons at the compass points, each
        /// pointing inward at the impact spot, with a centre dot. The most open of
        /// the four - it brackets the target without covering it.
        /// </summary>
        DiamondReticle,

        /// <summary>
        /// design3_funnel_pipper. A small ringed pipper with a broken horizontal
        /// bar, a rung ladder climbing above it and a heading capsule at the top.
        /// The only one of the four that is taller than it is wide.
        /// </summary>
        FunnelPipper,

        /// <summary>
        /// design4_segmented_rings. Two broken rings, the outer one with diagonal
        /// corner ticks, around a gapped centre cross and dot.
        /// </summary>
        SegmentedRings,
    }
}

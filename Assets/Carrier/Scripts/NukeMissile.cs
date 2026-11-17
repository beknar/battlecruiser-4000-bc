using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A Vrask nuclear missile, from the Maw's silos (NukeLauncher). It flies as a missile does
    /// (slower, and it turns lazily), and it is one: co-op shows it with the missiles, and point
    /// defence can shoot it down (Missile.toughness) before it gets there.
    ///
    /// In space it goes off as it reaches its target (or when its fuel runs out) in a blast
    /// `blastRadius` wide (about 1.2 km) that hurts every ship in it (HitKind.Nuclear, less the
    /// further off: ArtilleryShell.Blast), but in vacuum there is no air to carry a shock wave, so
    /// it does moderate damage: enough to break a frigate's shield, not to destroy one that is
    /// whole. Down on a world with air it is another thing: a Maw's nuclear strike on a city wipes
    /// it out (VraskBombardment). Shot down, or shown from another player's game (no damage), it
    /// only bursts.
    /// </summary>
    public class NukeMissile : Missile
    {
        [Tooltip("The blast's reach in space (m).")]
        public float blastRadius = 1200f;
        [Tooltip("The share of its damage at the blast's edge.")]
        [Range(0f, 1f)] public float edge = 0.3f;
        [Tooltip("The colour of its flash.")]
        public Color flashColor = new Color(1f, 0.92f, 0.75f);

        /// <summary>Nuclear blasts so far (in space), and the last.</summary>
        public static int Detonations { get; private set; }
        public static ArtilleryShell.BlastResult LastBlast { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCounts() { Detonations = 0; LastBlast = default; }

        protected override void Detonate()
        {
            if (Gone) return;
            Vector3 at = transform.position;
            if (damage > 0f)
            {
                Detonations++;
                LastBlast = ArtilleryShell.Blast(at, blastRadius, damage, edge, HitKind.Nuclear, owner);
            }
            ArtilleryShell.Show(at, blastRadius, flashColor, explosion);
            Finish(null);
        }
    }
}

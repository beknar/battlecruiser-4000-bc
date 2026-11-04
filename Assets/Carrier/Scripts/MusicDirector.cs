using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Plays the soundtrack: two arrangements of the same track ("Void" from the Space Science
    /// Fiction Music Pack), a calm one and a combat one, looping in sync. While weapons and
    /// explosions are being heard nearby it crossfades to the combat arrangement; once things
    /// have been quiet for a while it eases back to the calm one. Both loops are the same length,
    /// so the switch never jumps in the music.
    /// </summary>
    public class MusicDirector : MonoBehaviour
    {
        public float calmVolume = 0.30f;
        public float combatVolume = 0.34f;
        [Tooltip("Seconds without combat sounds before the music calms down.")]
        public float combatHold = 10f;
        public float fadeToCombat = 2.5f;
        public float fadeToCalm = 6f;

        /// <summary>0 = calm arrangement, 1 = combat arrangement.</summary>
        public static float CombatMix { get; private set; }
        public static bool Playing { get; private set; }

        AudioSource calm, combat;

        void Start()
        {
            var calmClip = SoundFX.LoadLibrary("Music/Music_Calm");
            var combatClip = SoundFX.LoadLibrary("Music/Music_Combat");
            if (calmClip == null || combatClip == null) { enabled = false; return; }
            calm = Source(calmClip[0]);
            combat = Source(combatClip[0]);
            calm.volume = calmVolume;
            combat.volume = 0f;
            calm.Play();
            combat.Play();
            Playing = true;
        }

        AudioSource Source(AudioClip clip)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.clip = clip;
            a.loop = true;
            a.spatialBlend = 0f;
            a.priority = 0;          // never stolen by effects
            a.dopplerLevel = 0f;
            a.playOnAwake = false;
            return a;
        }

        void Update()
        {
            if (calm == null) return;
            bool fighting = Time.time - SoundFX.LastCombatTime < combatHold;
            float target = fighting ? 1f : 0f;
            float rate = 1f / (target > CombatMix ? fadeToCombat : fadeToCalm);
            CombatMix = Mathf.MoveTowards(CombatMix, target, rate * Time.deltaTime);
            // equal-power crossfade, so the overall level holds steady through the switch
            float k = CombatMix * Mathf.PI * 0.5f;
            calm.volume = calmVolume * Mathf.Cos(k);
            combat.volume = combatVolume * Mathf.Sin(k);
            // keep the two loops locked together
            if (Mathf.Abs(combat.timeSamples - calm.timeSamples) > 2048) combat.timeSamples = calm.timeSamples;
        }
    }
}

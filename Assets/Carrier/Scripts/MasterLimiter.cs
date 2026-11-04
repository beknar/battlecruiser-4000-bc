using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Keeps the final mix from clipping when a lot happens at once (several explosions on top of
    /// the guns). Sits on the AudioListener, so it processes everything you hear. Quiet passages
    /// pass through untouched; above the threshold the level is turned down at once and eased
    /// back up over about a quarter of a second.
    /// </summary>
    public class MasterLimiter : MonoBehaviour
    {
        const float Threshold = 0.8f;
        float envelope, release;

        void Awake() => release = Mathf.Exp(-1f / (0.25f * AudioSettings.outputSampleRate));

        void OnAudioFilterRead(float[] data, int channels)
        {
            for (int i = 0; i < data.Length; i += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++) peak = Mathf.Max(peak, Mathf.Abs(data[i + c]));
                envelope = peak > envelope ? peak : envelope * release + peak * (1f - release);
                if (envelope <= Threshold) continue;
                float gain = Threshold / envelope;
                for (int c = 0; c < channels; c++) data[i + c] *= gain;
            }
        }
    }
}

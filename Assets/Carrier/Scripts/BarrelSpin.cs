using UnityEngine;

namespace CarrierGame
{
    /// <summary>Spins a rotary cannon's barrel cluster while its turret is firing.</summary>
    public class BarrelSpin : MonoBehaviour
    {
        public CarrierTurret turret;
        public float maxSpeed = 1800f;    // degrees per second
        [Tooltip("Muzzle flash shown (flickering) while the gun is firing.")]
        public Renderer flash;
        public Light flashLight;
        public float flashIntensity = 80f;

        float speed;
        Vector3 flashScale = Vector3.one;

        void Awake()
        {
            if (flash) { flashScale = flash.transform.localScale; flash.enabled = false; }
            if (flashLight) flashLight.enabled = false;
        }

        void Update()
        {
            bool firing = turret != null && Time.time - turret.LastFiredTime < 0.25f;
            speed = Mathf.MoveTowards(speed, firing ? maxSpeed : 0f, maxSpeed * 2.5f * Time.deltaTime);
            if (speed > 0f) transform.Rotate(0f, 0f, speed * Time.deltaTime, Space.Self);

            bool lit = turret != null && Time.time - turret.LastFiredTime < 0.09f;
            if (flash)
            {
                flash.enabled = lit;
                if (lit) flash.transform.localScale = Vector3.Scale(flashScale, new Vector3(Random.Range(0.7f, 1.2f), Random.Range(0.7f, 1.2f), Random.Range(0.6f, 1.4f)));
            }
            if (flashLight)
            {
                flashLight.enabled = lit;
                if (lit) flashLight.intensity = flashIntensity * Random.Range(0.6f, 1f);
            }
        }
    }
}

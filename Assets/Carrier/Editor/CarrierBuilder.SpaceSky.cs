using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The space sky (Shaders/SpaceSky.shader): one skybox material per star system, made from
    /// TC_Space (the far nebula, graded), OrbitalBase's night-sky masks (the galaxy's band of
    /// clouds and its bright knots, when they're staged) and stars made in the shader. The
    /// Testing System's is Materials/Space_Skybox.mat (every other space level uses it too); the
    /// Nine Hells' is Space_Skybox_NineHells.mat, turned and tinted so the two skies differ.
    /// </summary>
    public static partial class CarrierBuilder
    {
        /// <summary>One system's sky: what is set on its SpaceSky material (the shader's own
        /// defaults for the rest).</summary>
        class SkyLook
        {
            /// <summary>The whole sky's rotation (Euler degrees).</summary>
            public Vector3 sky;
            public Color nebulaTint = Color.white;
            public float nebulaContrast = 1.5f, nebulaSaturation = 1.2f;
            /// <summary>The clouds' band, in the sky's frame: (0, 0, 40) lays it along TC_Space's
            /// own band of the galaxy, which it adds to.</summary>
            public Vector3 cloudEuler = new Vector3(0f, 0f, 40f);
            /// <summary>Half width (degrees), repeats round, offset along, where the edge starts to fade.</summary>
            public Vector4 cloudBand = new Vector4(28f, 4f, 0f, 0.35f);
            public Color cloudTint, bandStars, knotTint;
            public Vector3 knotEuler;
            public Vector4 knotBand;
            public float cloudSpin = 0.01f, knotSpin = -0.015f, dustDarken = 0.3f;
            public float starSeed;
        }

        /// <summary>Kestrel's sky: the nebula much as it was (darker between, brighter in its
        /// band), the galaxy's clouds a cold blue over it, pale violet knots.</summary>
        static readonly SkyLook TestingSkyLook = new SkyLook
        {
            cloudTint = new Color(0.45f, 0.55f, 1.2f),
            bandStars = new Color(1.2f, 1.2f, 1.4f),
            knotTint = new Color(0.9f, 0.75f, 0.95f),
            knotEuler = new Vector3(6f, 25f, 46f),
            knotBand = new Vector4(34f, 3f, 0.3f, 0.35f),
        };

        /// <summary>The Nine Hells' sky: turned, so the galaxy crosses it elsewhere; the nebula
        /// warmer and harder, its clouds a dull ember red, the knots gold, and stars of their own.</summary>
        static readonly SkyLook NineHellsSkyLook = new SkyLook
        {
            sky = new Vector3(20f, 130f, -25f),
            nebulaTint = new Color(1.15f, 0.88f, 0.8f),
            nebulaContrast = 1.6f,
            nebulaSaturation = 1.1f,
            cloudBand = new Vector4(30f, 4f, 0.37f, 0.35f),
            cloudTint = new Color(1.25f, 0.45f, 0.25f),
            bandStars = new Color(1.4f, 1.2f, 1.0f),
            knotTint = new Color(1.0f, 0.8f, 0.5f),
            knotEuler = new Vector3(-8f, 60f, 34f),
            knotBand = new Vector4(30f, 3f, 0.6f, 0.35f),
            cloudSpin = -0.008f,
            knotSpin = 0.012f,
            dustDarken = 0.4f,
            starSeed = 37f,
        };

        /// <summary>A SpaceSky material saved as Materials/{name}.mat (one already there is
        /// changed in place, so it keeps its GUID and the scenes their reference to it). Without
        /// the masks staged it draws the nebula and the stars alone; without TC_Space, the stars.</summary>
        static Material SpaceSkyMaterial(string name, SkyLook look)
        {
            var shader = Shader.Find("Carrier/SpaceSky");
            if (shader == null)
            {
                Log($"{name}: no Carrier/SpaceSky shader; the sky is left as it was");
                return Load<Material>($"{MatDir}/{name}.mat");
            }
            var m = new Material(shader);
            // (the looks' colours are what the shader is to get: a material's colours are taken as
            // sRGB and made linear on the way to it, so they're set as their sRGB values)
            void Tint(string property, Color linear) => m.SetColor(property, linear.gamma);
            m.SetVector("_SkyEuler", look.sky);
            m.SetTexture("_NebulaTex", KitTexture("TC_Space.exr"));
            Tint("_NebulaTint", look.nebulaTint);
            m.SetFloat("_NebulaContrast", look.nebulaContrast);
            m.SetFloat("_NebulaSaturation", look.nebulaSaturation);
            var clouds = KitTexture("T_StarsMasks01.png");
            var knots = KitTexture("T_StarsMasks02.png");
            bool masks = clouds != null && knots != null;
            m.SetFloat("_Masks", masks ? 1f : 0f);
            if (masks) m.EnableKeyword("_SKY_MASKS");
            else m.DisableKeyword("_SKY_MASKS");
            m.SetTexture("_CloudTex", clouds);
            m.SetTexture("_KnotTex", knots);
            m.SetVector("_CloudEuler", look.cloudEuler);
            m.SetVector("_CloudBand", look.cloudBand);
            Tint("_CloudTint", look.cloudTint);
            Tint("_BandStarTint", look.bandStars);
            m.SetFloat("_DustDarken", look.dustDarken);
            m.SetFloat("_CloudSpin", look.cloudSpin);
            m.SetVector("_KnotEuler", look.knotEuler);
            m.SetVector("_KnotBand", look.knotBand);
            Tint("_KnotTint", look.knotTint);
            m.SetFloat("_KnotSpin", look.knotSpin);
            m.SetFloat("_StarSeed", look.starSeed);
            Log($"{name}: the space sky, nebula {(m.GetTexture("_NebulaTex") != null ? "TC_Space" : "missing (not staged)")}, " +
                $"galaxy masks {(masks ? "on" : "off (T_StarsMasks01/02 not staged)")}");
            return SaveMat(name, m);
        }
    }
}

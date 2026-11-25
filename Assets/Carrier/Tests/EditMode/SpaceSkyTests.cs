using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The space sky (Shaders/SpaceSky.shader, CarrierBuilder.SpaceSky): the shader builds,
    /// and each star system has a sky of its own made with it.</summary>
    public class SpaceSkyTests
    {
        const string ShaderName = "Carrier/SpaceSky";

        [Test]
        public void TheShaderBuildsWithEverythingTheBuilderSets()
        {
            var shader = Shader.Find(ShaderName);
            Assert.That(shader, Is.Not.Null, ShaderName);
            string messages = string.Join("; ", ShaderUtil.GetShaderMessages(shader).Select(m => $"{m.message} (line {m.line})"));
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, messages);
            foreach (var p in new[] { "_SkyEuler", "_NebulaTex", "_NebulaTint", "_NebulaContrast", "_NebulaSaturation", "_Masks", "_CloudTex", "_KnotTex",
                                      "_CloudEuler", "_CloudBand", "_CloudTint", "_BandStarTint", "_DustDarken", "_CloudSpin",
                                      "_KnotEuler", "_KnotBand", "_KnotTint", "_KnotSpin", "_StarSeed" })
                Assert.That(shader.FindPropertyIndex(p), Is.GreaterThanOrEqualTo(0), p);
        }

        [Test]
        public void EachSystemHasASkyOfItsOwn()
        {
            var testing = AssetDatabase.LoadAssetAtPath<Material>("Assets/Carrier/Materials/Space_Skybox.mat");
            var hells = AssetDatabase.LoadAssetAtPath<Material>("Assets/Carrier/Materials/Space_Skybox_NineHells.mat");
            Assert.That(testing, Is.Not.Null, "Space_Skybox.mat (made by Build Everything)");
            Assert.That(hells, Is.Not.Null, "Space_Skybox_NineHells.mat (made by Build Everything)");
            Assert.That(testing.shader.name, Is.EqualTo(ShaderName));
            Assert.That(hells.shader.name, Is.EqualTo(ShaderName));
            Assert.That(hells.GetVector("_SkyEuler"), Is.Not.EqualTo(testing.GetVector("_SkyEuler")), "the Nine Hells' sky is turned");
            Assert.That(hells.GetColor("_CloudTint"), Is.Not.EqualTo(testing.GetColor("_CloudTint")), "and tinted");
            Assert.That(hells.GetFloat("_StarSeed"), Is.Not.EqualTo(testing.GetFloat("_StarSeed")), "with stars of its own");
        }
    }
}

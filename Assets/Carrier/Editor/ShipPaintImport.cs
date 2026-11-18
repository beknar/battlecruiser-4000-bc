using UnityEditor;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>Import settings for the ship paint's decal atlases (Resources/Paint: the logos and
    /// the lettering, made by Tools/Textures/make_paint_atlases.py): one channel (red), linear,
    /// uncompressed (sharp edges, and nothing a web build can't read), mipmapped, clamped and not
    /// rescaled to a power of two (the shader finds each cell by its share of the atlas). The
    /// paint screen's copy of the logos (paint_logos_ui) is an ordinary RGBA texture.</summary>
    public class ShipPaintImport : AssetPostprocessor
    {
        const string Root = "Assets/Carrier/Resources/Paint/";

        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var ti = (TextureImporter)assetImporter;
            if (assetPath.EndsWith("_ui.png"))
            {
                // the paint screen's logos: white, the shapes in the alpha, for the GUI
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.isReadable = false;
                return;
            }
            ti.textureType = TextureImporterType.SingleChannel;
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            ti.SetTextureSettings(settings);
            ti.sRGBTexture = false;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 4;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.isReadable = false;
            ti.maxTextureSize = 2048;
        }
    }
}

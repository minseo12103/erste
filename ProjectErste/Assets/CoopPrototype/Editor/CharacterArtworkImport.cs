using UnityEditor;
using UnityEngine;

namespace ShapeCoop.Editor
{
    // Sprite.Create uses regions of this unmodified transparent source at runtime.
    [InitializeOnLoad]
    public sealed class CharacterArtworkImport : AssetPostprocessor
    {
        const string Path = "Assets/Resources/ShapeCoop/character.png";
        static CharacterArtworkImport() { EditorApplication.delayCall += EnsureSettings; }
        void OnPreprocessTexture()
        {
            if (assetPath == Path) Configure((TextureImporter)assetImporter);
        }
        static void Configure(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
        }
        static void EnsureSettings()
        {
            var importer = AssetImporter.GetAtPath(Path) as TextureImporter;
            if (importer == null) return;
            if (importer.textureType == TextureImporterType.Default && importer.npotScale == TextureImporterNPOTScale.None && importer.alphaIsTransparency && !importer.mipmapEnabled && importer.textureCompression == TextureImporterCompression.Uncompressed && importer.wrapMode == TextureWrapMode.Clamp && importer.maxTextureSize >= 2048) return;
            Configure(importer); importer.SaveAndReimport();
        }
    }
}

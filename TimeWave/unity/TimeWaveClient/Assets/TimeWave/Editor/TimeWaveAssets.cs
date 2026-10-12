using System.IO;
using UnityEditor;
using UnityEngine;

namespace TimeWave.Client.Editor
{
    /// <summary>Creates the assets the code-built scene needs (a lit material in Resources) when they are missing.</summary>
    [InitializeOnLoad]
    public static class TimeWaveAssets
    {
        public const string LitMaterialPath = "Assets/TimeWave/Resources/TimeWaveLit.mat";

        static TimeWaveAssets() => EditorApplication.delayCall += Ensure;

        public static void Ensure()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(LitMaterialPath) != null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(LitMaterialPath));
            var material = new Material(Shader.Find("Standard")) { name = "TimeWaveLit" };
            material.SetFloat("_Glossiness", 0.25f);
            AssetDatabase.CreateAsset(material, LitMaterialPath);
            AssetDatabase.SaveAssets();
        }
    }
}

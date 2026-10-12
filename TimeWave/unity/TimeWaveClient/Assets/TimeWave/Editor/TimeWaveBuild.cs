using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TimeWave.Client.Editor
{
    /// <summary>
    /// Builds a Windows player. From the menu, or headless:
    ///   Unity.exe -batchmode -quit -projectPath ... -executeMethod TimeWave.Client.Editor.TimeWaveBuild.BuildWindows [-buildOutput path\TimeWaveClient.exe]
    /// </summary>
    public static class TimeWaveBuild
    {
        public const string ScenePath = "Assets/TimeWave/TimeWave.unity";

        [MenuItem("TimeWave/Build Windows Player")]
        public static void BuildWindows()
        {
            string output = Arg("-buildOutput") ?? "Build/Windows/TimeWaveClient.exe";
            TimeWaveAssets.Ensure();
            EnsureScene();

            PlayerSettings.productName = "TimeWave Client";
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            var summary = report.summary;
            Debug.Log($"TimeWaveBuild: {summary.result}, {summary.totalErrors} errors, output {Path.GetFullPath(output)}");
            if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>A scene with just a camera and light; the TimeWave object is created at runtime by the bootstrap.</summary>
        static void EnsureScene()
        {
            if (File.Exists(ScenePath)) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}

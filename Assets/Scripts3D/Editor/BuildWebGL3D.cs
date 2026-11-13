using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Сборка WebGL для GitHub Pages: результат кладётся в папку docs/ в корне репозитория,
// Pages раздаёт сайт прямо из docs/ ветки main.
// Tools > Space Shooter 3D > Build WebGL for GitHub Pages
public static class BuildWebGL3D
{
    const string OutputFolder = "docs";
    const string ScenePath = "Assets/Scenes/Game3D.unity";

    [MenuItem("Tools/Space Shooter 3D/Build WebGL for GitHub Pages")]
    public static void Build()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            EditorUtility.DisplayDialog("WebGL не установлен",
                "Для этой версии Unity не установлен модуль Web Build Support.\n\n" +
                "Unity Hub > Installs > (версия) > Add modules > Web Build Support", "OK");
            return;
        }

        // GitHub Pages не отдаёт заголовок Content-Encoding, поэтому:
        // gzip-сжатие + распаковка в браузере (Decompression Fallback)
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.runInBackground = true;

        string root = Directory.GetParent(Application.dataPath).FullName;
        string outPath = Path.Combine(root, OutputFolder);
        if (Directory.Exists(outPath)) Directory.Delete(outPath, true);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result == BuildResult.Succeeded)
        {
            File.WriteAllText(Path.Combine(outPath, ".nojekyll"), "");
            Debug.Log("[Space Shooter 3D] WebGL собран в " + outPath + " (" + (report.summary.totalSize / (1024 * 1024)) + " МБ)");
        }
        else
        {
            Debug.LogError("[Space Shooter 3D] Сборка WebGL не удалась: " + report.summary.result);
        }
    }
}

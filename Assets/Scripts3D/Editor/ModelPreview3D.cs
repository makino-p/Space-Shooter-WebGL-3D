using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Служебный инструмент: рендерит модели (сверху, сбоку, спереди) в PNG, чтобы проверить ориентацию и масштаб.
// Запуск: создать файл Temp/model_preview_request.txt — строка 1: папка для PNG, далее пути к ассетам.
[InitializeOnLoad]
public static class ModelPreview3D
{
    const string RequestPath = "Temp/model_preview_request.txt";

    static ModelPreview3D()
    {
        EditorApplication.update += Poll;
    }

    static double _nextPoll;

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < _nextPoll) return;
        _nextPoll = EditorApplication.timeSinceStartup + 2.0;
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;

        string[] lines = File.ReadAllLines(RequestPath);
        File.Delete(RequestPath);
        if (lines.Length < 2) return;

        string outDir = lines[0].Trim();
        Directory.CreateDirectory(outDir);
        List<string> report = new List<string>();
        for (int i = 1; i < lines.Length; i++)
        {
            string path = lines[i].Trim();
            if (path.Length == 0) continue;
            report.Add(Render(path, outDir));
        }
        File.WriteAllLines(Path.Combine(outDir, "report.txt"), report);
        Debug.Log("[ModelPreview3D] Готово: " + outDir);
    }

    static string Render(string assetPath, string outDir)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (asset == null) return assetPath + " : NOT FOUND";

        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject go = (GameObject)Object.Instantiate(asset);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;

            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            bool has = false;
            int tris = 0;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
            }

            GameObject lightGo = new GameObject("Light");
            SceneManager.MoveGameObjectToScene(lightGo, scene);
            Light l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.3f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject camGo = new GameObject("Cam");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            Camera cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.16f);
            float size = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) * 1.15f;
            cam.orthographicSize = size;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = size * 20f;

            const int res = 384;
            RenderTexture rt = new RenderTexture(res, res, 24);
            Texture2D sheet = new Texture2D(res * 3, res, TextureFormat.RGB24, false);
            Vector3[] dirs = { Vector3.up, Vector3.right, Vector3.forward };     // сверху, справа, спереди (+Z)
            Vector3[] ups = { Vector3.forward, Vector3.up, Vector3.up };
            for (int v = 0; v < 3; v++)
            {
                camGo.transform.position = b.center + dirs[v] * size * 6f;
                camGo.transform.rotation = Quaternion.LookRotation(-dirs[v], ups[v]);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                Texture2D tmp = new Texture2D(res, res, TextureFormat.RGB24, false);
                tmp.ReadPixels(new Rect(0, 0, res, res), 0, 0);
                tmp.Apply();
                sheet.SetPixels(v * res, 0, res, res, tmp.GetPixels());
                Object.DestroyImmediate(tmp);
            }
            RenderTexture.active = null;
            cam.targetTexture = null;
            sheet.Apply();

            string file = Path.Combine(outDir, Path.GetFileNameWithoutExtension(assetPath).Replace(' ', '_') + ".png");
            File.WriteAllBytes(file, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            rt.Release();
            Object.DestroyImmediate(rt);

            return string.Format("{0} : size={1} center={2} tris={3} rootEuler={4} rootScale={5}",
                assetPath, b.size.ToString("F3"), b.center.ToString("F3"), tris,
                asset.transform.localEulerAngles.ToString("F1"), asset.transform.localScale.ToString("F3"));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Саундтреки: все аудиофайлы из Assets/Audio/Soundtracks назначаются в Game3D.MusicTracks
// (игра включает случайный трек на каждой волне). Импорт настраивается под музыку:
// потоковая загрузка (не держит весь трек в памяти) и сжатие Vorbis.
public static class Soundtracks3D
{
    public const string Folder = "Assets/Audio/Soundtracks";

    public static List<AudioClip> FindTracks()
    {
        List<AudioClip> clips = new List<AudioClip>();
        if (!Directory.Exists(Folder)) return clips;
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ConfigureImport(path);
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null) clips.Add(clip);
        }
        clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return clips;
    }

    static void ConfigureImport(string path)
    {
        AudioImporter ai = AssetImporter.GetAtPath(path) as AudioImporter;
        if (ai == null) return;
        AudioImporterSampleSettings s = ai.defaultSampleSettings;
        if (s.loadType == AudioClipLoadType.Streaming && s.compressionFormat == AudioCompressionFormat.Vorbis) return;
        s.loadType = AudioClipLoadType.Streaming;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.7f;
        ai.defaultSampleSettings = s;
        ai.SaveAndReimport();
    }

    public static bool SceneNeedsAssets(string sceneYaml)
    {
        if (FindTracks().Count == 0) return false;
        int i = sceneYaml.IndexOf("MusicTracks:");
        return i < 0 || !sceneYaml.Substring(i, System.Math.Min(90, sceneYaml.Length - i)).Contains("guid:");
    }

    public static void AssignTo(Game3D game)
    {
        game.MusicTracks = FindTracks().ToArray();
    }
}

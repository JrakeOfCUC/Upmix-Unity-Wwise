using System;
using System.IO;
using UnityEngine;

[Serializable]
internal sealed class SpatialLayout
{
    [Serializable]
    public sealed class Entry
    {
        public string track;
        public string sourceSong;
        public Vector3 position;
        public bool muted;
    }

    public int version;
    public string song;
    public Entry[] objects;
    public bool hallPreset;

    public static string DefaultPath(string directory, string songName) =>
        Path.Combine(directory, songName + ".spatial.json");

    public static SpatialLayout Load(string path) =>
        JsonUtility.FromJson<SpatialLayout>(File.ReadAllText(path));

    public void Save(string path)
    {
        path = Path.GetFullPath(path);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonUtility.ToJson(this, true));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}

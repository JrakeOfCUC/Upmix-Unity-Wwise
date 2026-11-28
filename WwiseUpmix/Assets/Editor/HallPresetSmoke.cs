using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// Run in a disposable batch Editor session; never saves the opened scene.
public class HallPresetSmoke
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Call(object player, string name, params object[] args) =>
        player.GetType().GetMethod(name, Flags).Invoke(player, args);
    private static object Field(object player, string name) => player.GetType().GetField(name, Flags).GetValue(player);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Pump()
    {
        for (int i = 0; i < 8; i++) { AkSoundEngineController.Instance.LateUpdate(); Thread.Sleep(30); }
    }
    [DllImport("AkUnitySoundEngine", EntryPoint = "CSharp_GetGameObjectAuxSendValues")]
    private static extern int GetSends(ulong emitter, IntPtr values, ref uint count);

    [UnityTest]
    public IEnumerator EndToEnd()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        for (int i = 0; i < 30; i++) yield return null;
        Verify();
        yield return new ExitPlayMode();
    }

    private static void Verify()
    {
        Require(AkUnitySoundEngine.IsInitialized(), "Wwise initialization failed");
        var player = UnityEngine.Object.FindObjectOfType<SpatialStemPlayer>();
        Pump();
        var song = Field(player, "layoutSong");
        object initial = Call(player, "CaptureLayout");
        var preset = Call(player, "MakeHallLayout", song);
        Require((bool)Call(player, "InstallLayout", preset, song, false, true), "Apply failed: " + Field(player, "switchError"));
        Pump();
        Require((bool)Field(player, "hallPreset"), "Preset flag not set");
        var returns = (GameObject[])Field(player, "hallReturns");
        Require(returns.Length == 8, "Missing returns");
        var stems = (Array)Field(player, "stems");
        Require(stems.Length == 8, "Missing stage objects");
        object instrumental = stems.GetValue(7);
        Require((bool)instrumental.GetType().GetField("muted").GetValue(instrumental), "Instrumental must be muted");
        object first = stems.GetValue(0);
        var emitter = (GameObject)first.GetType().GetField("emitter").GetValue(first);
        Require(emitter.transform.position == new Vector3(0,1.6f,-6), "Wrong vocal position");
        IntPtr buffer = Marshal.AllocHGlobal(AkAuxSendValue.GetSizeOf() * 8);
        try
        {
            uint count = 8;
            Require((AKRESULT)GetSends(AkUnitySoundEngine.GetAkGameObjectID(emitter), buffer, ref count) == AKRESULT.AK_Success && count == 8,
                "Wwise did not register all eight sends");
            player.GetType().GetField("bypassRoom", Flags).SetValue(player, true);
            Call(player, "ApplyMix"); Pump(); count = 8;
            GetSends(AkUnitySoundEngine.GetAkGameObjectID(emitter), buffer, ref count);
            Require(count == 0, "Bypass left active sends");
            player.GetType().GetField("bypassRoom", Flags).SetValue(player, false);
        }
        finally { Marshal.FreeHGlobal(buffer); }
        string json = JsonUtility.ToJson(Call(player, "CaptureLayout"));
        Require(json.Contains("\"hallPreset\":true"), "Save lost preset");
        object saved = JsonUtility.FromJson(json, preset.GetType());
        Require((bool)Call(player, "InstallLayout", saved, song, false, true), "Round trip failed");
        Pump();
        Call(player, "SetPaused", true);
        Require((bool)Call(player, "InstallLayout", saved, song, false, true), "Paused application failed");
        Require((bool)Field(player, "paused"), "Preset lost paused state");
        Call(player, "SwitchSong", -1, Enum.ToObject(song.GetType(), 1));
        Pump();
        Require((bool)Field(player, "hallPreset") && Field(player, "layoutSong").ToString() == "SeeYouAgain", "Song change lost preset");
        // Restore on the new song, and exercise legacy v1 loading.
        initial.GetType().GetField("song").SetValue(initial, "SeeYouAgain");
        initial.GetType().GetField("version").SetValue(initial, 1);
        Require((bool)Call(player, "InstallLayout", initial, Field(player, "layoutSong"), false, false), "Legacy restore failed");
        Require(!(bool)Field(player, "hallPreset"), "Restore left hall routing enabled");
        AkUnitySoundEngine.StopAll(); Pump();
        Debug.Log("HALL_PRESET_SMOKE_PASS: apply during playback, eight native sends, bypass, JSON round-trip, paused apply, song switch, legacy restore.");
    }
}

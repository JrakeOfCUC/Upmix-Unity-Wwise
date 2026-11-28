using System;
using System.Runtime.InteropServices;
using UnityEngine;

public sealed partial class SpatialStemPlayer
{
    // Energy-normalized late sends (four side/rear + two quieter height returns).
    private static readonly float[] HallReturnGains = { .7f, .7f, .45f, .45f, .45f, .45f, .3f, .3f };
    private bool hallPreset;
    private LoadedSoundBank hallBank;
    private float initialMinimapRadius;
    private GameObject[] hallReturns;
    private HallSendBuffer hallSends;

    private SpatialLayout MakeHallLayout(Song song)
    {
        var entries = new SpatialLayout.Entry[HallStage.Length];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = new SpatialLayout.Entry { track = HallStage[i].Track,
                sourceSong = song.ToString(), position = HallStage[i].Position, muted = HallStage[i].Muted };
        return new SpatialLayout { version = 2, song = song.ToString(), objects = entries, hallPreset = true };
    }

    private void DrawHallPresetControls()
    {
        GUILayout.Label("位置预设（替换当前对象，可恢复初始布局）");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("应用音乐厅 B"))
        {
            InstallLayout(MakeHallLayout(layoutSong), layoutSong, false);
            GUIUtility.ExitGUI();
        }
        if (GUILayout.Button("恢复初始布局"))
        {
            var layout = new SpatialLayout { version = 2, song = layoutSong.ToString(),
                objects = Array.ConvertAll(defaultLayout.objects, e => new SpatialLayout.Entry {
                    track = e.track, sourceSong = layoutSong.ToString(), position = e.position }) };
            InstallLayout(layout, layoutSong, false);
            GUIUtility.ExitGUI();
        }
        GUILayout.EndHorizontal();
        GUILayout.Label(hallPreset ? "当前：音乐厅 B · 器乐默认静音 · 8 个固定空间返回" : "当前：普通布局");
        if (hallPreset) GUILayout.Label("E1/E2：侧前反射；R1—R4：侧后混响；R5/R6：上方混响");
    }

    private bool PrepareHall()
    {
        if (hallReturns != null) return true;
        if (Camera.main == null) { switchError = "未找到主摄像机。"; return false; }
        hallBank = LoadedSoundBank.Load("SpatialHallPreset", out var result);
        if (hallBank == null)
        {
            switchError = "音乐厅预设音库未就绪，请重新生成 SoundBank：" + result;
            return false;
        }
        hallReturns = new GameObject[HallReturns.Length];
        try
        {
            hallSends = new HallSendBuffer(hallReturns.Length);
            var listenerIds = new[] { AkUnitySoundEngine.GetAkGameObjectID(Camera.main.gameObject) };
            using (var mono = new AkChannelConfig(1, 4)) // AK_SPEAKER_FRONT_CENTER
            for (int i = 0; i < hallReturns.Length; i++)
            {
                var node = hallReturns[i] = new GameObject(HallReturns[i].Name);
                node.transform.position = HallReturns[i].Position;
                node.transform.SetParent(transform, true);
                node.AddComponent<AkGameObj>().isEnvironmentAware = false;
                CheckHallResult(AkUnitySoundEngine.SetListeners(node, listenerIds, 1));
                // Receive a mono feed without input-direction panning; the AuxBus positions its output.
                CheckHallResult(AkUnitySoundEngine.SetListenerSpatialization(node, false, mono));
            }
            return true;
        }
        catch (Exception ex)
        {
            switchError = "无法初始化音乐厅空间返回：" + ex.Message;
            DisposeHall();
            return false;
        }
    }

    private static void CheckHallResult(AKRESULT result)
    {
        if (result != AKRESULT.AK_Success) throw new InvalidOperationException(result.ToString());
    }

    private void SetHallActive(bool active)
    {
        hallPreset = active;
        minimapRadius = active ? Mathf.Max(8, initialMinimapRadius) : initialMinimapRadius;
        // Keep game objects registered while old effect tails finish. No new sends when inactive.
    }

    private AKRESULT SetHallSends(GameObject emitter, float gain)
    {
        if (hallReturns == null || hallSends == null) return AKRESULT.AK_Fail;
        for (int i = 0; i < hallReturns.Length; i++)
            hallSends.Set(i, hallReturns[i], AkUnitySoundEngine.GetIDFromString("SpatialHall_" + i), gain * HallReturnGains[i]);
        return hallSends.Apply(emitter, gain > 0 ? hallReturns.Length : 0);
    }

    private void DisposeHall()
    {
        hallSends?.Dispose();
        hallSends = null;
        if (hallReturns != null) foreach (var node in hallReturns) if (node != null) Destroy(node);
        hallReturns = null;
        hallBank?.Dispose();
        hallBank = null;
    }

    // Wwise's Unity AkAuxSendArray is capped at four entries. The SDK accepts a variable-size
    // array, so use its exported setters/size instead of assuming a native struct layout.
    private sealed class HallSendBuffer : IDisposable
    {
        [DllImport("AkUnitySoundEngine", EntryPoint = "CSharp_AkAuxSendValue_Set")]
        private static extern void SetValue(IntPtr value, ulong listener, uint bus, float gain);
        [DllImport("AkUnitySoundEngine", EntryPoint = "CSharp_SetGameObjectAuxSendValues")]
        private static extern int SetValues(ulong emitter, IntPtr values, uint count);
        private IntPtr buffer;
        private readonly int stride = AkAuxSendValue.GetSizeOf();
        public HallSendBuffer(int count) { buffer = Marshal.AllocHGlobal(checked(stride * count)); }
        public void Set(int i, GameObject listener, uint bus, float gain) =>
            SetValue(IntPtr.Add(buffer, i * stride), AkUnitySoundEngine.GetAkGameObjectID(listener), bus, gain);
        public AKRESULT Apply(GameObject emitter, int count) =>
            (AKRESULT)SetValues(AkUnitySoundEngine.GetAkGameObjectID(emitter), buffer, (uint)count);
        public void Dispose() { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; }
    }
}

using System;
using System.Runtime.InteropServices;
using UnityEngine;

public sealed partial class SpatialStemPlayer
{
    // World coordinates from docs/spatial-layout/layout-design.json, fixed around C=(0,1,-10).
    private static readonly Vector3[] HallStagePositions = {
        new Vector3(0,1.6f,-6), new Vector3(1.8f,1.6f,-4.5f),
        new Vector3(-1.3f,1.1f,-5), new Vector3(0,1.3f,-3.5f),
        new Vector3(3,1.3f,-5.5f), new Vector3(-3,1.2f,-5.5f),
        new Vector3(-2.2f,1.5f,-3.5f), new Vector3(0,1.3f,-4.8f)
    };
    private static readonly string[] HallTracks = {
        "Vocal", "Harmony", "Bass", "Drums", "Guitar", "Piano", "Other", "Instrumental"
    };
    private static readonly Vector3[] HallReturnPositions = {
        new Vector3(-3.6f,2.2f,-8), new Vector3(3.6f,2.2f,-8),
        new Vector3(-4,1.8f,-10), new Vector3(4,1.8f,-10),
        new Vector3(-3,2,-13), new Vector3(3,2,-13),
        new Vector3(-2,4.2f,-11), new Vector3(2,4.2f,-11)
    };
    private static readonly string[] HallReturnNames = {
        "E1 左早反射", "E2 右早反射", "R1 左侧混响", "R2 右侧混响",
        "R3 左后混响", "R4 右后混响", "R5 左上混响", "R6 右上混响"
    };
    // Energy-normalized late sends (four side/rear + two quieter height returns).
    private static readonly float[] HallReturnGains = { .7f, .7f, .45f, .45f, .45f, .45f, .3f, .3f };
    private bool hallPreset, ownsHallBank;
    private uint hallBank;
    private float initialMinimapRadius;
    private GameObject[] hallReturns;
    private HallSendBuffer hallSends;

    private Layout MakeHallLayout(Song song)
    {
        var entries = new Entry[HallTracks.Length];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = new Entry { track = "Play_Stem_" + HallTracks[i],
                sourceSong = song.ToString(), position = HallStagePositions[i], muted = i == 7 };
        return new Layout { version = 2, song = song.ToString(), objects = entries, hallPreset = true };
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
            var layout = new Layout { version = 2, song = layoutSong.ToString(),
                objects = Array.ConvertAll(defaultLayout.objects, e => new Entry {
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
        var result = AkUnitySoundEngine.LoadBank("SpatialHallPreset", out hallBank);
        if (result != AKRESULT.AK_Success && result != AKRESULT.AK_BankAlreadyLoaded)
        {
            switchError = "音乐厅预设音库未就绪，请重新生成 SoundBank：" + result;
            return false;
        }
        ownsHallBank = result == AKRESULT.AK_Success;
        hallReturns = new GameObject[HallReturnPositions.Length];
        try
        {
            hallSends = new HallSendBuffer(hallReturns.Length);
            var listenerIds = new[] { AkUnitySoundEngine.GetAkGameObjectID(Camera.main.gameObject) };
            using (var mono = new AkChannelConfig(1, 4)) // AK_SPEAKER_FRONT_CENTER
            for (int i = 0; i < hallReturns.Length; i++)
            {
                var node = hallReturns[i] = new GameObject(HallReturnNames[i]);
                node.transform.position = HallReturnPositions[i];
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

    private void DrawHallMap(Camera camera, Vector2 center, float radius, float range, Quaternion heading)
    {
        if (!hallPreset) return;
        for (int i = 0; i < HallReturnPositions.Length; i++)
        {
            Vector3 p = heading * (HallReturnPositions[i] - camera.transform.position);
            if (new Vector2(p.x, p.z).sqrMagnitude > range * range) continue;
            Vector2 point = center + new Vector2(p.x, -p.z) * (radius / range);
            MapRect(new Rect(point.x - 8, point.y - 7, 16, 14), i < 2 ? new Color(.65f,.36f,.08f) : new Color(.4f,.28f,.65f));
            GUI.Label(new Rect(point.x - 8, point.y - 7, 16, 14), i < 2 ? "E" + (i+1) : "R" + (i-1), mapMarker);
        }
    }

    private void DisposeHall()
    {
        hallSends?.Dispose();
        hallSends = null;
        if (hallReturns != null) foreach (var node in hallReturns) if (node != null) Destroy(node);
        hallReturns = null;
        if (ownsHallBank) AkUnitySoundEngine.UnloadBank(hallBank, IntPtr.Zero);
        ownsHallBank = false;
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

using System;
using UnityEngine;

public sealed partial class SpatialStemPlayer
{
    private readonly SourceMap sourceMap = new SourceMap();
    public bool MixerOpen { get; set; }
    private Vector2 mixerScroll;
    private const int PickerClosed = -2;
    private int picker = PickerClosed;
    private int effectsRow = -1;
    private static readonly string[] RoomNames = { "小房间", "音乐厅" };
    private bool editing;
    private int sourceTrack, selected;
    private Vector2 editorScroll;
    private string layoutPath;

    private void UpdateLayoutPath()
    {
        layoutPath = SpatialLayout.DefaultPath(Application.persistentDataPath, layoutSong.ToString());
    }

    private static float EffectSlider(string label, float value, float min, float max, bool percent = false)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(128));
        float next = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.Label(percent ? $"{next:P0}" : $"{next:+0.0;-0.0;0.0} dB", GUILayout.Width(65));
        GUILayout.EndHorizontal();
        return next;
    }

    private void OnGUI()
    {
        if (!MixerOpen)
        {
            sourceMap.Draw(stems, minimapRadius, hallPreset);
            return;
        }
        int previousDepth = GUI.depth;
        GUI.depth = -100;
        var previousColor = GUI.color;
        GUI.color = new Color(0.06f, 0.08f, 0.11f, 0.95f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previousColor;

        float width = Mathf.Min(620f, Screen.width - 12f);
        float height = Mathf.Min(540f, Screen.height - 12f);
        var area = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
        GUILayout.BeginArea(area, GUI.skin.window);
        GUILayout.Label("分轨混音    ·    Esc 返回场景");
        bool active = IsPlaying;
        bool wasEnabled = GUI.enabled;
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        wasEnabled &= outputSelector.Draw(PauseForSpeakerCheck);
#endif
        GUILayout.BeginHorizontal();
        GUI.enabled = wasEnabled && catalog != null && (!active || paused);
        if (GUILayout.Button("播放"))
        {
            if (active) SetPaused(false);
            else PlayFromStart();
        }
        GUI.enabled = wasEnabled && active && !paused;
        if (GUILayout.Button("暂停")) SetPaused(true);
        GUI.enabled = wasEnabled;
        GUILayout.EndHorizontal();
        editing = GUILayout.Toggle(editing, "空间音源编辑 · " + Songs[(int)layoutSong].Title, GUI.skin.button);
        if (editing && catalog != null)
        {
            DrawEditor();
            GUILayout.EndArea();
            GUI.depth = previousDepth;
            return;
        }
        if (GUILayout.Button("整首换歌")) picker = WholeSong;
        if (picker != PickerClosed)
        {
            GUILayout.Label(picker == WholeSong ? "选择整首歌曲" : "选择分轨 · " + stems[picker].label);
            int position = active && picker != WholeSong ? Position() : 0;
            for (int j = 0; j < Songs.Length; j++)
            {
                if (j % 2 == 0) GUILayout.BeginHorizontal();
                Song song = (Song)j;
                GUI.enabled = wasEnabled && (picker == WholeSong ||
                    (picker >= 0 && EventFor(stems[picker], song) != null &&
                     position >= 0 && position < Songs[j].DurationMs && stems[picker].song != song));
                if (GUILayout.Button(Songs[j].Title))
                {
                    SwitchSong(picker, song);
                    picker = PickerClosed;
                }
                if (j % 2 == 1) GUILayout.EndHorizontal();
            }
            GUI.enabled = wasEnabled;
            if (GUILayout.Button("关闭选曲")) picker = PickerClosed;
        }
        if (switchError != null) GUILayout.Label(switchError);
        mixerScroll = GUILayout.BeginScrollView(mixerScroll);
        GUILayout.BeginHorizontal();
        bool bypass = GUILayout.Toggle(bypassEffects, "旁通全部效果", GUI.skin.button);
        bool roomOff = GUILayout.Toggle(bypassRoom, "旁通混响", GUI.skin.button);
        GUI.enabled = wasEnabled && !hallPreset;
        int preset = GUILayout.Toolbar(roomPreset, RoomNames);
        GUI.enabled = wasEnabled;
        GUILayout.EndHorizontal();
        if (hallPreset) GUILayout.Label("音乐厅 B · 2 路早反射 + 6 路空间混响");
        bool changed = bypass != bypassEffects || roomOff != bypassRoom || preset != roomPreset;
        bypassEffects = bypass;
        bypassRoom = roomOff;
        roomPreset = preset;
        for (int i = 0; i < stems.Length; i++)
        {
            var stem = stems[i];
            GUILayout.BeginHorizontal(GUILayout.Height(32f));
            GUILayout.Label(stem.label, GUILayout.Width(64f));
            if (GUILayout.Button(Songs[(int)stem.song].Title, GUILayout.Width(130f))) picker = i;
            if (EventFor(stem, stem.song) == null)
            {
                GUILayout.Label("本曲无此分轨");
                GUILayout.EndHorizontal();
                continue;
            }
            float db = GUILayout.HorizontalSlider(stem.decibels, -60f, 0f, GUILayout.MinWidth(48f));
            GUILayout.Label($"{db:0.0} dB", GUILayout.Width(62f));
            bool mute = GUILayout.Toggle(stem.muted, "Mute", GUI.skin.button, GUILayout.Width(50f));
            bool solo = GUILayout.Toggle(stem.solo, "Solo", GUI.skin.button, GUILayout.Width(50f));
            if (GUILayout.Button("效果", GUILayout.Width(48))) effectsRow = effectsRow == i ? -1 : i;
            GUILayout.EndHorizontal();
            if (effectsRow == i)
            {
                var send = EffectSlider("混响发送量", stem.roomSend, 0, 1, true);
                bool eqOff = GUILayout.Toggle(stem.bypassEq, "旁通此轨 EQ");
                Vector3 eq = new Vector3(
                    EffectSlider("低频   160 Hz", stem.eqDb.x, -12, 12),
                    EffectSlider("中频   1.2 kHz", stem.eqDb.y, -12, 12),
                    EffectSlider("高频   6 kHz", stem.eqDb.z, -12, 12));
                if (GUILayout.Button("均衡器归零")) eq = Vector3.zero;
                changed |= eq != stem.eqDb || eqOff != stem.bypassEq || send != stem.roomSend;
                stems[i].roomSend = send;
                stems[i].bypassEq = eqOff;
                stems[i].eqDb = eq;
            }
            if (db == stem.decibels && mute == stem.muted && solo == stem.solo) continue;
            stems[i].decibels = db;
            stems[i].muted = mute;
            stems[i].solo = solo;
            changed = true;
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        GUI.depth = previousDepth;
        if (changed) ApplyMix();
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

    private void DrawEditor()
    {
        editorScroll = GUILayout.BeginScrollView(editorScroll);
        DrawHallPresetControls();
        GUILayout.Label("从当前曲目的分轨新建音源");
        sourceTrack = GUILayout.SelectionGrid(sourceTrack, Array.ConvertAll(catalog, s => s.label), 4);
        Stem template = catalog[sourceTrack];
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && EventFor(template, layoutSong) != null;
        if (GUILayout.Button("新建空间音源"))
        {
            var entry = new SpatialLayout.Entry { track = template.eventName, sourceSong = layoutSong.ToString(),
                position = Camera.main != null ? Camera.main.transform.position + Camera.main.transform.forward * 2 : Vector3.zero };
            InstallLayout(new SpatialLayout { version = 1, song = layoutSong.ToString(), objects = new[] { entry } }, layoutSong, true);
            GUIUtility.ExitGUI();
        }
        GUI.enabled = enabled;
        GUILayout.Label($"空间对象（{stems.Length}）");
        if (stems.Length > 0)
        {
            selected = GUILayout.SelectionGrid(selected, sourceMap.Legend, 4);
            Transform emitter = stems[selected].emitter.transform;
            Vector3 p = emitter.position;
            emitter.position = new Vector3(Axis("X", p.x), Axis("Y", p.y), Axis("Z", p.z));
            if (GUILayout.Button("移除选中对象"))
            {
                RemoveTrack(selected);
                GUIUtility.ExitGUI();
            }
        }
        GUILayout.Label("当前曲目布局文件（JSON）");
        layoutPath = GUILayout.TextField(layoutPath);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("保存布局"))
        {
            try
            {
                CaptureLayout().Save(layoutPath);
                switchError = "布局已保存。";
            }
            catch (Exception ex) { switchError = ex.Message; }
        }
        if (GUILayout.Button("导入布局"))
        {
            try
            {
                InstallLayout(SpatialLayout.Load(layoutPath), layoutSong, false);
            }
            catch (Exception ex) { switchError = ex.Message; }

            GUIUtility.ExitGUI();
        }
        GUILayout.EndHorizontal();
        if (switchError != null) GUILayout.Label(switchError);
        GUILayout.EndScrollView();
    }

    private static float Axis(string label, float value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label}  {value:F2} m", GUILayout.Width(100));
        float next = GUILayout.HorizontalSlider(value, Mathf.Min(-20, value), Mathf.Max(20, value));
        GUILayout.EndHorizontal();
        return next;
    }
}

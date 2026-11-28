using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[DefaultExecutionOrder(100)]
public sealed partial class SpatialStemPlayer : MonoBehaviour
{
    private readonly struct SongInfo
    {
        public readonly string Title;
        public readonly int DurationMs;
        public readonly string Bank;
        public readonly string EventSuffix;
        public readonly bool HasHarmony;

        public SongInfo(string title, int durationMs, string bank, string eventSuffix, bool hasHarmony)
        {
            Title = title;
            DurationMs = durationMs;
            Bank = bank;
            EventSuffix = eventSuffix;
            HasHarmony = hasHarmony;
        }
    }

    [SerializeField, InspectorName("启动歌曲")] private Song startupSong;

    [Serializable]
    private struct Stem
    {
        public GameObject emitter;
        public string eventName;
        [NonSerialized] public float decibels;
        [NonSerialized] public bool muted;
        [NonSerialized] public bool solo;
        [NonSerialized] public string label;
        [NonSerialized] public Song song;
        [NonSerialized] public Vector3 eqDb;
        [NonSerialized] public bool bypassEq;
        [NonSerialized] public float roomSend;
    }

    [SerializeField] private Stem[] stems;
    [SerializeField, Min(0.1f), InspectorName("小地图半径（米）")]
    private float minimapRadius = 6f;
    private readonly SourceMap sourceMap = new SourceMap();
    public bool MixerOpen { get; set; }
    private Vector2 mixerScroll;
    private readonly Dictionary<Song, LoadedSoundBank> loadedBanks = new Dictionary<Song, LoadedSoundBank>();
    private const int WholeSong = -1;
    private const int PickerClosed = -2;
    private int picker = PickerClosed;
    private string switchError;
    private uint[] playingIds;
    private bool paused;
    private int effectsRow = -1;
    private int roomPreset;
    private static readonly string[] RoomNames = { "小房间", "音乐厅" };
    private bool bypassEffects;
    private bool bypassRoom;
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
    private LoadedSoundBank checkBank;
    private uint checkId;
    private float checkStarted;
    private readonly MacOutputSelector outputSelector = new MacOutputSelector();
    private static readonly string[] CheckChannels =
        { "L", "R", "C", "LFE", "Lrs", "Rrs", "Ls", "Rs", "Ltf", "Rtf", "Ltr", "Rtr" };
#endif

    private void Start()
    {
        for (int i = 0; i < stems.Length; i++)
        {
            var marker = stems[i].emitter.GetComponentInChildren<SpatialStemLabel>();
            stems[i].label = marker != null ? marker.DisplayName : stems[i].emitter.name;
            stems[i].song = startupSong;
            stems[i].roomSend = 0.12f;
        }

        sourceMap.Refresh(stems);
        catalog = (Stem[])stems.Clone();
        layoutSong = startupSong;
        defaultLayout = CaptureLayout();
        initialMinimapRadius = minimapRadius;
        UpdateLayoutPath();
        playingIds = new uint[stems.Length];
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        checkBank = LoadedSoundBank.Load("MTRX714_Check", out _);
        MixerOpen = true;
#else
        if (LoadSong(startupSong)) PlayFromStart();
#endif
    }

    private static string EventFor(Stem stem, Song song)
    {
        var info = Songs[(int)song];
        if (stem.eventName == "Play_Stem_Harmony" && !info.HasHarmony) return null;
        return info.EventSuffix.Length == 0 ? stem.eventName : stem.eventName + info.EventSuffix;
    }

    private bool LoadSong(Song song)
    {
        if (loadedBanks.ContainsKey(song)) return true;
        var bank = LoadedSoundBank.Load(Songs[(int)song].Bank, out var result);
        if (bank == null)
        {
            switchError = $"无法加载《{Songs[(int)song].Title}》：{result}";
            Debug.LogError(switchError, this);
            return false;
        }
        loadedBanks.Add(song, bank);
        return true;
    }

    private void ReleaseBank(Song song)
    {
        loadedBanks[song].Dispose();
        loadedBanks.Remove(song);
    }

    private int Position()
    {
        foreach (uint id in playingIds)
            if (id != 0 && AkUnitySoundEngine.GetSourcePlayPosition(id, out int ms) == AKRESULT.AK_Success)
                return ms;
        return -1;
    }

    private uint Post(Stem stem, Song song)
    {
        return AkUnitySoundEngine.PostEvent(EventFor(stem, song), stem.emitter,
            (uint)AkCallbackType.AK_EndOfEvent,
            OnStemEnded, null);
    }

    // The caller owns any nonzero ID, including a post whose seek failed.
    private bool TryPostAt(Stem stem, Song song, int position, out uint id)
    {
        id = Post(stem, song);
        if (id == 0 || position < 0 || position >= Songs[(int)song].DurationMs) return false;
        if (position > 0 && AkUnitySoundEngine.SeekOnEvent(EventFor(stem, song),
                stem.emitter, position, false, id) != AKRESULT.AK_Success) return false;
        if (paused) AkUnitySoundEngine.ExecuteActionOnPlayingID(AkActionOnEventType.AkActionOnEventType_Pause, id);
        return true;
    }

    private static void StopIds(uint[] ids)
    {
        foreach (uint id in ids)
            if (id != 0) AkUnitySoundEngine.StopPlayingID(id);
    }

    private void PlayFromStart()
    {
        switchError = null;
        uint[] next = new uint[stems.Length];
        for (int i = 0; i < stems.Length; i++)
        {
            Song song = stems[i].song;
            if (EventFor(stems[i], song) == null) continue;
            if (!LoadSong(song) || (next[i] = Post(stems[i], song)) == 0)
            {
                StopIds(next);
                switchError = switchError ?? $"无法播放{stems[i].label}";
                return;
            }
        }
        playingIds = next;
        paused = false;
        ApplyMix();
    }

    private void SwitchSong(int target, Song song)
    {
        switchError = null;
        if (target == WholeSong)
        {
            var layout = new SpatialLayout { version = 1, song = song.ToString(), objects = new SpatialLayout.Entry[defaultLayout.objects.Length] };
            for (int i = 0; i < layout.objects.Length; i++)
                layout.objects[i] = new SpatialLayout.Entry { track = catalog[i].eventName, sourceSong = layout.song,
                    position = defaultLayout.objects[i].position };
            if (hallPreset) layout = MakeHallLayout(song);
            if (InstallLayout(layout, song, false, false))
            {
                layoutSong = song;
                UpdateLayoutPath();
            }
            return;
        }
        if (!LoadSong(song)) return;
        bool active = Array.Exists(playingIds, id => id != 0);
        int position = active ? Position() : 0;
        uint next = 0;
        if (position < 0 || position >= Songs[(int)song].DurationMs)
            switchError = "无法在当前播放位置接入这条分轨。";
        else if (active && EventFor(stems[target], song) != null &&
                 !TryPostAt(stems[target], song, position, out next))
            switchError = $"无法切换{stems[target].label}。";

        if (switchError == null)
        {
            if (playingIds[target] != 0) AkUnitySoundEngine.StopPlayingID(playingIds[target]);
            playingIds[target] = next;
            stems[target].song = song;
            ApplyMix();
        }
        else if (next != 0) AkUnitySoundEngine.StopPlayingID(next);
        ReleaseUnusedBanks();
    }

    private void OnStemEnded(object cookie, AkCallbackType type, AkCallbackInfo info)
    {
        if (!(info is AkEventCallbackInfo ended) || playingIds == null) return;
        int index = Array.IndexOf(playingIds, ended.playingID);
        if (index >= 0) playingIds[index] = 0;
    }

    private void SetPaused(bool pause)
    {
        var action = pause ? AkActionOnEventType.AkActionOnEventType_Pause
                           : AkActionOnEventType.AkActionOnEventType_Resume;
        foreach (uint id in playingIds)
            if (id != 0) AkUnitySoundEngine.ExecuteActionOnPlayingID(action, id);
        paused = pause;
    }

    private void ApplyMix()
    {
        if (playingIds == null || Camera.main == null) return;
        var listener = Camera.main.gameObject;
        bool anySolo = Array.Exists(stems, stem => stem.solo && EventFor(stem, stem.song) != null);
        using (var send = new AkAuxSendArray())
        foreach (var stem in stems)
        {
            bool audible = !stem.muted && (!anySolo || stem.solo);
            float gain = audible ? Mathf.Pow(10f, stem.decibels / 20f) : 0f;
            var result = AkUnitySoundEngine.SetGameObjectOutputBusVolume(stem.emitter, listener, gain);
            if (result != AKRESULT.AK_Success)
                Debug.LogWarning($"Could not set {stem.eventName} gain: {result}", this);
            if (!loadedBanks.ContainsKey(stem.song) || EventFor(stem, stem.song) == null) continue;
            var eq = bypassEffects || stem.bypassEq ? Vector3.zero : stem.eqDb;
            SetBand(stem.emitter, "StemEQLow", eq.x);
            SetBand(stem.emitter, "StemEQMid", eq.y);
            SetBand(stem.emitter, "StemEQHigh", eq.z);
            send.Reset();
            if (hallPreset)
            {
                var hallResult = SetHallSends(stem.emitter,
                    !bypassEffects && !bypassRoom ? gain * stem.roomSend : 0f);
                if (hallResult != AKRESULT.AK_Success)
                    Debug.LogWarning($"Could not set hall sends: {hallResult}", this);
                continue;
            }
            if (!bypassEffects && !bypassRoom && gain * stem.roomSend > 0f)
                send.Add(listener, AkUnitySoundEngine.GetIDFromString(roomPreset == 0 ? "StemSmallRoom" : "StemHall"),
                    gain * stem.roomSend);
            var sendResult = send.SetValues(stem.emitter);
            if (sendResult != AKRESULT.AK_Success)
                Debug.LogWarning($"Could not set {stem.label} reverb send: {sendResult}", this);
        }
    }

    private void SetBand(GameObject emitter, string parameter, float db)
    {
        var result = AkUnitySoundEngine.SetRTPCValue(parameter, db, emitter, 30);
        if (result != AKRESULT.AK_Success) Debug.LogWarning($"{parameter}: {result}", this);
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
        bool active = playingIds != null && Array.Exists(playingIds, id => id != 0);
        bool wasEnabled = GUI.enabled;
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        outputSelector.Draw(checkId == 0);
        GUI.enabled = wasEnabled && checkBank != null && checkId == 0;
        if (GUILayout.Button("测试 12 路音箱 · 每秒一路"))
        {
            if (active && !paused) SetPaused(true);
            checkStarted = Time.realtimeSinceStartup;
            checkId = AkUnitySoundEngine.PostEvent("Play_MTRX714_Check", Camera.main.gameObject,
                (uint)AkCallbackType.AK_EndOfEvent, OnCheckEnded, null);
        }
        GUI.enabled = wasEnabled;
        if (checkBank == null) GUILayout.Label("测试音库未加载");
        if (checkId != 0)
        {
            int index = Mathf.Clamp((int)(Time.realtimeSinceStartup - checkStarted), 0, 11);
            GUILayout.Label($"测试 {index + 1}/12   {CheckChannels[index]}");
            wasEnabled = false;
        }
#endif
        GUILayout.BeginHorizontal();
        GUI.enabled = wasEnabled && playingIds != null && (!active || paused);
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

    private Stem[] catalog;
    private SpatialLayout defaultLayout;
    private Song layoutSong;
    private bool editing;
    private int sourceTrack, selected;
    private Vector2 editorScroll;
    private string layoutPath;

    private void UpdateLayoutPath()
    {
        layoutPath = SpatialLayout.DefaultPath(Application.persistentDataPath, layoutSong.ToString());
    }

    private SpatialLayout CaptureLayout()
    {
        return new SpatialLayout { version = 2, song = layoutSong.ToString(), hallPreset = hallPreset, objects = Array.ConvertAll(stems,
            s => new SpatialLayout.Entry { track = s.eventName, sourceSong = s.song.ToString(), position = s.emitter.transform.position, muted = s.muted }) };
    }

    private Stem[] ResolveLayout(SpatialLayout layout, Song song, bool sync)
    {
        if (layout == null || (layout.version != 1 && layout.version != 2) || layout.song != song.ToString() || layout.objects == null)
            throw new InvalidDataException("布局版本或所属曲目不匹配。");
        var resolved = new Stem[layout.objects.Length];
        for (int i = 0; i < resolved.Length; i++)
        {
            var entry = layout.objects[i];
            int t = entry == null ? -1 : Array.FindIndex(catalog, s => s.eventName == entry.track);
            if (t < 0 || !Enum.TryParse(entry.sourceSong, out Song source) || !Enum.IsDefined(typeof(Song), source)
                || float.IsNaN(entry.position.sqrMagnitude) || float.IsInfinity(entry.position.sqrMagnitude))
                throw new InvalidDataException("布局含有无效的音轨或坐标。");
            Stem stem = catalog[t];
            if (!sync)
            {
                int previous = Array.FindIndex(stems, s => s.eventName == entry.track);
                if (previous >= 0) stem = stems[previous];
            }
            stem.emitter = catalog[t].emitter;
            stem.song = source;
            if (layout.version == 2) stem.muted = entry.muted;
            resolved[i] = stem;
        }
        return resolved;
    }

    private bool InstallLayout(SpatialLayout layout, Song song, bool append, bool sync = true)
    {
        var created = new List<Stem>();
        var ids = new List<uint>();
        bool hadHall = hallReturns != null;
        try
        {
            Stem[] resolved = ResolveLayout(layout, song, sync);
            if (!append && layout.version == 2 && layout.hallPreset && !PrepareHall())
                throw new IOException(switchError);
            bool active = Array.Exists(playingIds, id => id != 0);
            int ms = active && sync ? Position() : 0;
            for (int i = 0; i < resolved.Length; i++)
            {
                Stem stem = resolved[i];
                Song source = stem.song;
                string evt = EventFor(stem, source);
                if (evt != null && !LoadSong(source)) throw new IOException(switchError);
                stem.emitter = Instantiate(stem.emitter, layout.objects[i].position, stem.emitter.transform.rotation, transform);
                created.Add(stem);
                stem.emitter.SetActive(true);
                uint id = 0;
                bool posted = !active || evt == null || TryPostAt(stem, source, ms, out id);
                ids.Add(id);
                if (!posted) throw new IOException("无法将音源同步到当前播放位置。");
            }
        }
        catch (Exception ex)
        {
            StopIds(ids.ToArray());
            foreach (var s in created) Destroy(s.emitter);
            if (!hadHall && hallReturns != null) DisposeHall();
            switchError = ex.Message;
            ReleaseUnusedBanks();
            return false;
        }
        if (append)
        {
            created.InsertRange(0, stems);
            ids.InsertRange(0, playingIds);
        }
        else
        {
            StopIds(playingIds);
            foreach (var s in stems) Retire(s);
        }
        stems = created.ToArray();
        playingIds = ids.ToArray();
        if (!append) SetHallActive(layout.version == 2 && layout.hallPreset);
        selected = append ? stems.Length - 1 : 0;
        RefreshObjects();
        return true;
    }

    private void ReleaseUnusedBanks()
    {
        foreach (Song bank in new List<Song>(loadedBanks.Keys))
            if (!Array.Exists(stems, s => s.song == bank)) ReleaseBank(bank);
    }

    private void Retire(Stem stem)
    {
        stem.emitter.SetActive(false);
        if (!Array.Exists(catalog, s => s.emitter == stem.emitter)) Destroy(stem.emitter);
    }

    private void RefreshObjects()
    {
        sourceMap.Refresh(stems);
        effectsRow = -1;
        picker = PickerClosed;
        switchError = null;
        ReleaseUnusedBanks();
        ApplyMix();
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
                if (playingIds[selected] != 0) AkUnitySoundEngine.StopPlayingID(playingIds[selected]);
                Retire(stems[selected]);
                var objects = new List<Stem>(stems);
                var ids = new List<uint>(playingIds);
                objects.RemoveAt(selected);
                ids.RemoveAt(selected);
                stems = objects.ToArray();
                playingIds = ids.ToArray();
                selected = Mathf.Max(0, selected - 1);
                RefreshObjects();
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

    private void OnDestroy()
    {
        DisposeHall();
        bool initialized = AkUnitySoundEngine.IsInitialized();
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        if (initialized && checkId != 0) AkUnitySoundEngine.StopPlayingID(checkId);
        checkBank?.Dispose();
#endif
        if (initialized && playingIds != null) StopIds(playingIds);
        foreach (Song song in new List<Song>(loadedBanks.Keys)) ReleaseBank(song);
    }
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
    private void OnCheckEnded(object cookie, AkCallbackType type, AkCallbackInfo info)
    {
        if (info is AkEventCallbackInfo ended && ended.playingID == checkId) checkId = 0;
    }
#endif
}

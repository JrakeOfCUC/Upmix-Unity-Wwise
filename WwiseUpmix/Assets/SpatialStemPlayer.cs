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
    private GUIStyle mapText;
    private GUIStyle mapMarker;
    private string[] markerNumbers;
    private string[] legendNames;
    public bool MixerOpen { get; set; }
    private Vector2 mixerScroll;
    private readonly Dictionary<Song, uint> loadedBanks = new Dictionary<Song, uint>();
    private readonly HashSet<Song> ownedBanks = new HashSet<Song>();
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
    private uint checkBankId;
    private bool ownsCheckBank;
    private bool checkBankReady;
    private uint checkId;
    private float checkStarted;
    private readonly MacOutputSelector outputSelector = new MacOutputSelector();
    private static readonly string[] CheckChannels =
        { "L", "R", "C", "LFE", "Lrs", "Rrs", "Ls", "Rs", "Ltf", "Rtf", "Ltr", "Rtr" };
#endif

    private void Start()
    {
        markerNumbers = new string[stems.Length];
        legendNames = new string[stems.Length];
        for (int i = 0; i < stems.Length; i++)
        {
            var marker = stems[i].emitter.GetComponentInChildren<SpatialStemLabel>();
            stems[i].label = marker != null ? marker.DisplayName : stems[i].emitter.name;
            stems[i].song = startupSong;
            stems[i].roomSend = 0.12f;
            markerNumbers[i] = (i + 1).ToString();
            legendNames[i] = markerNumbers[i] + "  " + stems[i].label;
        }

        catalog = (Stem[])stems.Clone();
        layoutSong = startupSong;
        defaultLayout = CaptureLayout();
        initialMinimapRadius = minimapRadius;
        UpdateLayoutPath();
        playingIds = new uint[stems.Length];
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        var checkResult = AkUnitySoundEngine.LoadBank("MTRX714_Check", out checkBankId);
        ownsCheckBank = checkResult == AKRESULT.AK_Success;
        checkBankReady = ownsCheckBank || checkResult == AKRESULT.AK_BankAlreadyLoaded;
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
        string bank = Songs[(int)song].Bank;
        var result = AkUnitySoundEngine.LoadBank(bank, out uint id);
        if (result != AKRESULT.AK_Success && result != AKRESULT.AK_BankAlreadyLoaded)
        {
            switchError = $"无法加载《{Songs[(int)song].Title}》：{result}";
            Debug.LogError(switchError, this);
            return false;
        }
        loadedBanks.Add(song, id);
        if (result == AKRESULT.AK_Success) ownedBanks.Add(song);
        return true;
    }

    private void ReleaseBank(Song song)
    {
        if (ownedBanks.Remove(song))
            AkUnitySoundEngine.UnloadBank(loadedBanks[song], IntPtr.Zero);
        loadedBanks.Remove(song);
    }

    private int Position()
    {
        foreach (uint id in playingIds)
            if (id != 0 && AkUnitySoundEngine.GetSourcePlayPosition(id, out int ms) == AKRESULT.AK_Success)
                return ms;
        return -1;
    }

    private uint Post(int index, Song song)
    {
        return Post(stems[index], song);
    }

    private uint Post(Stem stem, Song song)
    {
        return AkUnitySoundEngine.PostEvent(EventFor(stem, song), stem.emitter,
            (uint)AkCallbackType.AK_EndOfEvent,
            OnStemEnded, null);
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
            if (!LoadSong(song) || (next[i] = Post(i, song)) == 0)
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
            var layout = new Layout { version = 1, song = song.ToString(), objects = new Entry[defaultLayout.objects.Length] };
            for (int i = 0; i < layout.objects.Length; i++)
                layout.objects[i] = new Entry { track = catalog[i].eventName, sourceSong = layout.song,
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
        int position = active && target != WholeSong ? Position() : 0;
        uint[] next = new uint[stems.Length];
        if (position < 0 || position >= Songs[(int)song].DurationMs)
            switchError = "无法在当前播放位置接入这条分轨。";
        for (int i = 0; active && switchError == null && i < stems.Length; i++)
        {
            if (target != WholeSong && target != i) continue;
            string evt = EventFor(stems[i], song);
            if (evt == null) continue;
            uint id = next[i] = Post(i, song);
            if (id == 0 || (position > 0 && AkUnitySoundEngine.SeekOnEvent(evt,
                    stems[i].emitter, position, false, id) != AKRESULT.AK_Success))
                switchError = $"无法切换{stems[i].label}。";
            else if (paused)
                AkUnitySoundEngine.ExecuteActionOnPlayingID(AkActionOnEventType.AkActionOnEventType_Pause, id);
        }

        if (switchError == null)
        {
            for (int i = 0; i < stems.Length; i++)
            {
                if (target != WholeSong && target != i) continue;
                if (playingIds[i] != 0) AkUnitySoundEngine.StopPlayingID(playingIds[i]);
                playingIds[i] = next[i];
                stems[i].song = song;
            }
            ApplyMix();
        }
        else StopIds(next);

        foreach (Song bank in new List<Song>(loadedBanks.Keys))
            if (!Array.Exists(stems, stem => stem.song == bank)) ReleaseBank(bank);
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

    private static void MapRect(Rect rect, Color tint)
    {
        GUI.color = tint;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void DrawMinimap()
    {
        if (Event.current.type != EventType.Repaint || legendNames == null) return;
        Camera camera = Camera.main;
        if (camera == null) return;
        if (mapText == null)
        {
            mapText = new GUIStyle(GUI.skin.label) { fontSize = 12, padding = new RectOffset() };
            mapText.normal.textColor = Color.white;
            mapMarker = new GUIStyle(mapText) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }

        const float size = 184f;
        float height = size + 28f + 17f * ((stems.Length + 1) / 2);
        float scale = Mathf.Min(1f, (Screen.width - 12f) / size, (Screen.height - 12f) / height);
        if (scale <= 0f) return;
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        int previousDepth = GUI.depth;
        GUI.depth = -50;
        GUI.matrix = Matrix4x4.TRS(new Vector3(Screen.width - size * scale - 6f,
            Screen.height - height * scale - 6f, 0), Quaternion.identity, new Vector3(scale, scale, 1));
        MapRect(new Rect(0, 0, size, height), new Color(0.04f, 0.08f, 0.11f, 0.92f));
        float range = Mathf.Max(0.1f, minimapRadius);
        GUI.Label(new Rect(8, 4, size - 16, 18), $"音源方位   ·   {range:0.#} m", mapText);

        Vector2 center = new Vector2(size / 2, 24 + size / 2);
        float radius = size / 2 - 10;
        var grid = new Color(0.27f, 0.38f, 0.42f);
        MapRect(new Rect(center.x - radius, center.y, 2 * radius, 1), grid);
        MapRect(new Rect(center.x, center.y - radius, 1, 2 * radius), grid);
        Quaternion heading = Quaternion.Euler(0, -camera.transform.eulerAngles.y, 0);
        var ink = new Color(0.10f, 0.42f, 0.47f);
        for (int i = 0; i < stems.Length; i++)
        {
            var emitter = stems[i].emitter;
            if (emitter == null || !emitter.activeInHierarchy) continue;
            Vector3 offset = heading * (emitter.transform.position - camera.transform.position);
            Vector2 ground = new Vector2(offset.x, -offset.z);
            if (ground.sqrMagnitude > range * range) continue;
            Vector2 point = center + ground * (radius / range);
            var badge = new Rect(point.x - 6, point.y - 7, 12, 14);
            MapRect(badge, ink);
            GUI.Label(badge, markerNumbers[i], mapMarker);
        }
        // Small upward chevron marks the listener and its forward direction.
        var listener = new Color(1f, 0.78f, 0.35f);
        for (int y = 0; y < 4; y++)
            MapRect(new Rect(center.x - y, center.y - 3 + y, 1 + 2 * y, 1), listener);
        for (int i = 0; i < legendNames.Length; i++)
            GUI.Label(new Rect(8 + (i % 2) * size / 2, size + 24 + (i / 2) * 17,
                size / 2 - 8, 17), legendNames[i], mapText);
        DrawHallMap(camera, center, radius, range, heading);
        GUI.matrix = previousMatrix;
        GUI.color = previousColor;
        GUI.depth = previousDepth;
    }

    private void OnGUI()
    {
        if (!MixerOpen)
        {
            DrawMinimap();
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
        GUI.enabled = wasEnabled && checkBankReady && checkId == 0;
        if (GUILayout.Button("测试 12 路音箱 · 每秒一路"))
        {
            if (active && !paused) SetPaused(true);
            checkStarted = Time.realtimeSinceStartup;
            checkId = AkUnitySoundEngine.PostEvent("Play_MTRX714_Check", Camera.main.gameObject,
                (uint)AkCallbackType.AK_EndOfEvent, OnCheckEnded, null);
        }
        GUI.enabled = wasEnabled;
        if (!checkBankReady) GUILayout.Label("测试音库未加载");
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

    [Serializable] private class Entry
    {
        public string track;
        public string sourceSong;
        public Vector3 position;
        public bool muted;
    }

    [Serializable] private class Layout
    {
        public int version;
        public string song;
        public Entry[] objects;
        public bool hallPreset;
    }

    private Stem[] catalog;
    private Layout defaultLayout;
    private Song layoutSong;
    private bool editing;
    private int sourceTrack, selected;
    private Vector2 editorScroll;
    private string layoutPath;

    private void UpdateLayoutPath()
    {
        layoutPath = Path.Combine(Application.persistentDataPath, layoutSong + ".spatial.json");
    }

    private Layout CaptureLayout()
    {
        return new Layout { version = 2, song = layoutSong.ToString(), hallPreset = hallPreset, objects = Array.ConvertAll(stems,
            s => new Entry { track = s.eventName, sourceSong = s.song.ToString(), position = s.emitter.transform.position, muted = s.muted }) };
    }

    private bool InstallLayout(Layout layout, Song song, bool append, bool sync = true)
    {
        var created = new List<Stem>();
        var ids = new List<uint>();
        try
        {
            if (layout == null || (layout.version != 1 && layout.version != 2) || layout.song != song.ToString() || layout.objects == null)
                throw new InvalidDataException("布局版本或所属曲目不匹配。");
            if (!append && layout.version == 2 && layout.hallPreset && !PrepareHall())
                throw new IOException(switchError);
            bool active = Array.Exists(playingIds, id => id != 0);
            int ms = active && sync ? Position() : 0;
            foreach (Entry entry in layout.objects)
            {
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
                string evt = EventFor(stem, source);
                if (evt != null && !LoadSong(source)) throw new IOException(switchError);
                stem.emitter = Instantiate(stem.emitter, entry.position, stem.emitter.transform.rotation, transform);
                stem.emitter.SetActive(true);
                created.Add(stem);
                uint id = active && evt != null ? Post(stem, source) : 0;
                ids.Add(id);
                if (active && evt != null)
                {
                    if (id == 0 || ms < 0 || ms >= Songs[(int)source].DurationMs || (ms > 0 &&
                        AkUnitySoundEngine.SeekOnEvent(evt, stem.emitter, ms, false, id) != AKRESULT.AK_Success))
                        throw new IOException("无法将音源同步到当前播放位置。");
                    if (paused) AkUnitySoundEngine.ExecuteActionOnPlayingID(AkActionOnEventType.AkActionOnEventType_Pause, id);
                }
            }
        }
        catch (Exception ex)
        {
            StopIds(ids.ToArray());
            foreach (var s in created) Destroy(s.emitter);
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
        markerNumbers = new string[stems.Length];
        legendNames = new string[stems.Length];
        for (int i = 0; i < stems.Length; i++)
        {
            markerNumbers[i] = (i + 1).ToString();
            legendNames[i] = markerNumbers[i] + "  " + stems[i].label;
        }
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
            var entry = new Entry { track = template.eventName, sourceSong = layoutSong.ToString(),
                position = Camera.main != null ? Camera.main.transform.position + Camera.main.transform.forward * 2 : Vector3.zero };
            InstallLayout(new Layout { version = 1, song = layoutSong.ToString(), objects = new[] { entry } }, layoutSong, true);
            GUIUtility.ExitGUI();
        }
        GUI.enabled = enabled;
        GUILayout.Label($"空间对象（{stems.Length}）");
        if (stems.Length > 0)
        {
            selected = GUILayout.SelectionGrid(selected, legendNames, 4);
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
                string path = Path.GetFullPath(layoutPath);
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temp, JsonUtility.ToJson(CaptureLayout(), true));
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                switchError = "布局已保存。";
            }
            catch (Exception ex) { switchError = ex.Message; }
        }
        if (GUILayout.Button("导入布局"))
        {
            try
            {

                InstallLayout(JsonUtility.FromJson<Layout>(File.ReadAllText(layoutPath)), layoutSong, false);
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
        if (!AkUnitySoundEngine.IsInitialized()) return;
        DisposeHall();
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        if (checkId != 0) AkUnitySoundEngine.StopPlayingID(checkId);
        if (ownsCheckBank) AkUnitySoundEngine.UnloadBank(checkBankId, IntPtr.Zero);
#endif
        if (playingIds != null)
            foreach (var id in playingIds)
                if (id != 0) AkUnitySoundEngine.StopPlayingID(id);
        foreach (Song song in new List<Song>(loadedBanks.Keys)) ReleaseBank(song);
    }
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
    private void OnCheckEnded(object cookie, AkCallbackType type, AkCallbackInfo info)
    {
        if (info is AkEventCallbackInfo ended && ended.playingID == checkId) checkId = 0;
    }
#endif
}

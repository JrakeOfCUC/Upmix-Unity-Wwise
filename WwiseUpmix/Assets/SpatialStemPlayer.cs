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
        [NonSerialized] public uint playingId;
        [NonSerialized] public Vector3 eqDb;
        [NonSerialized] public bool bypassEq;
        [NonSerialized] public float roomSend;
    }

    [SerializeField] private Stem[] stems;
    [SerializeField, Min(0.1f), InspectorName("小地图半径（米）")]
    private float minimapRadius = 6f;
    private readonly Dictionary<Song, LoadedSoundBank> loadedBanks = new Dictionary<Song, LoadedSoundBank>();
    private const int WholeSong = -1;
    private string switchError;
    private bool IsPlaying => catalog != null && Array.Exists(stems, stem => stem.playingId != 0);
    private bool paused;
    private int roomPreset;
    private bool bypassEffects;
    private bool bypassRoom;
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
    private readonly MacOutputSelector outputSelector = new MacOutputSelector();
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
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        outputSelector.Initialize();
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
        foreach (Stem stem in stems)
            if (stem.playingId != 0 && AkUnitySoundEngine.GetSourcePlayPosition(stem.playingId, out int ms) == AKRESULT.AK_Success)
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

    private static void StopStems(IEnumerable<Stem> tracks)
    {
        foreach (Stem track in tracks)
            if (track.playingId != 0) AkUnitySoundEngine.StopPlayingID(track.playingId);
    }

    private void PlayFromStart()
    {
        switchError = null;
        var next = (Stem[])stems.Clone();
        for (int i = 0; i < stems.Length; i++)
        {
            Song song = stems[i].song;
            if (EventFor(stems[i], song) == null) continue;
            if (!LoadSong(song) || (next[i].playingId = Post(stems[i], song)) == 0)
            {
                StopStems(next);
                switchError = switchError ?? $"无法播放{stems[i].label}";
                return;
            }
        }
        stems = next;
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
        bool active = IsPlaying;
        int position = active ? Position() : 0;
        uint next = 0;
        if (position < 0 || position >= Songs[(int)song].DurationMs)
            switchError = "无法在当前播放位置接入这条分轨。";
        else if (active && EventFor(stems[target], song) != null &&
                 !TryPostAt(stems[target], song, position, out next))
            switchError = $"无法切换{stems[target].label}。";

        if (switchError == null)
        {
            if (stems[target].playingId != 0) AkUnitySoundEngine.StopPlayingID(stems[target].playingId);
            stems[target].playingId = next;
            stems[target].song = song;
            ApplyMix();
        }
        else if (next != 0) AkUnitySoundEngine.StopPlayingID(next);
        ReleaseUnusedBanks();
    }

    private void OnStemEnded(object cookie, AkCallbackType type, AkCallbackInfo info)
    {
        if (!(info is AkEventCallbackInfo ended) || catalog == null) return;
        int index = Array.FindIndex(stems, stem => stem.playingId == ended.playingID);
        if (index >= 0) stems[index].playingId = 0;
    }

    private void SetPaused(bool pause)
    {
        var action = pause ? AkActionOnEventType.AkActionOnEventType_Pause
                           : AkActionOnEventType.AkActionOnEventType_Resume;
        foreach (Stem stem in stems)
            if (stem.playingId != 0) AkUnitySoundEngine.ExecuteActionOnPlayingID(action, stem.playingId);
        paused = pause;
    }

    private void ApplyMix()
    {
        if (catalog == null || Camera.main == null) return;
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

    private Stem[] catalog;
    private SpatialLayout defaultLayout;
    private Song layoutSong;

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
            stem.playingId = 0;
            if (layout.version == 2) stem.muted = entry.muted;
            resolved[i] = stem;
        }
        return resolved;
    }

    private bool InstallLayout(SpatialLayout layout, Song song, bool append, bool sync = true)
    {
        Stem[] created = null;
        bool hadHall = hallReturns != null;
        try
        {
            Stem[] resolved = ResolveLayout(layout, song, sync);
            created = new Stem[resolved.Length];
            if (!append && layout.version == 2 && layout.hallPreset && !PrepareHall())
                throw new IOException(switchError);
            bool active = IsPlaying;
            int ms = active && sync ? Position() : 0;
            for (int i = 0; i < resolved.Length; i++)
            {
                Stem stem = resolved[i];
                Song source = stem.song;
                string evt = EventFor(stem, source);
                if (evt != null && !LoadSong(source)) throw new IOException(switchError);
                stem.emitter = Instantiate(stem.emitter, layout.objects[i].position, stem.emitter.transform.rotation, transform);
                created[i] = stem;
                stem.emitter.SetActive(true);
                if (active && evt != null && !TryPostAt(stem, source, ms, out created[i].playingId))
                    throw new IOException("无法将音源同步到当前播放位置。");
            }
        }
        catch (Exception ex)
        {
            if (created != null)
            {
                StopStems(created);
                foreach (var stem in created) if (stem.emitter != null) Destroy(stem.emitter);
            }
            if (!hadHall && hallReturns != null) DisposeHall();
            switchError = ex.Message;
            ReleaseUnusedBanks();
            return false;
        }
        if (append)
        {
            int previousCount = stems.Length;
            Array.Resize(ref stems, previousCount + created.Length);
            Array.Copy(created, 0, stems, previousCount, created.Length);
        }
        else
        {
            StopStems(stems);
            foreach (var s in stems) Retire(s);
            stems = created;
        }
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

    private void RemoveTrack(int index)
    {
        if (stems[index].playingId != 0) AkUnitySoundEngine.StopPlayingID(stems[index].playingId);
        Retire(stems[index]);
        var objects = new List<Stem>(stems);
        objects.RemoveAt(index);
        stems = objects.ToArray();
        selected = Mathf.Max(0, index - 1);
        RefreshObjects();
    }

    private void OnDestroy()
    {
        DisposeHall();
        bool initialized = AkUnitySoundEngine.IsInitialized();
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        outputSelector.Dispose();
#endif
        if (initialized && catalog != null) StopStems(stems);
        foreach (Song song in new List<Song>(loadedBanks.Keys)) ReleaseBank(song);
    }
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
    private void PauseForSpeakerCheck()
    {
        if (IsPlaying && !paused) SetPaused(true);
    }
#endif
}

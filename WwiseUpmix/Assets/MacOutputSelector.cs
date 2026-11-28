#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

internal sealed class MacOutputSelector : IDisposable
{
    // The native Wwise count is in/out. The generated C# overload exposes it as out only.
    [DllImport("AkUnitySoundEngine", EntryPoint = "CSharp_GetDeviceList__SWIG_0")]
    private static extern AKRESULT GetDevices(uint company, uint plugin, ref uint count, IntPtr buffer);

    private const uint SystemSink = 174;
    private readonly List<uint> ids = new List<uint>();
    private readonly List<string> names = new List<string>();
    private Vector2 scroll;
    private bool expanded;
    private string selection = "系统默认";
    private string message;
    private string status = "查询中…";
    private ulong outputId;
    private float nextQuery;

    private LoadedSoundBank checkBank;
    private uint checkId;
    private float checkStarted;
    private static readonly string[] CheckChannels =
        { "L", "R", "C", "LFE", "Lrs", "Rrs", "Ls", "Rs", "Ltf", "Rtf", "Ltr", "Rtr" };

    public void Initialize()
    {
        checkBank = LoadedSoundBank.Load("MTRX714_Check", out _);
    }

    public void Dispose()
    {
        if (checkId != 0 && AkUnitySoundEngine.IsInitialized()) AkUnitySoundEngine.StopPlayingID(checkId);
        checkBank?.Dispose();
    }

    private void OnCheckEnded(object cookie, AkCallbackType type, AkCallbackInfo info)
    {
        if (info is AkEventCallbackInfo ended && ended.playingID == checkId) checkId = 0;
    }

    public bool Draw(Action beforeCheck)
    {
        DrawDevicePicker(checkId == 0);
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && checkBank != null && checkId == 0;
        if (GUILayout.Button("测试 12 路音箱 · 每秒一路"))
        {
            beforeCheck();
            checkStarted = Time.realtimeSinceStartup;
            checkId = AkUnitySoundEngine.PostEvent("Play_MTRX714_Check", Camera.main.gameObject,
                (uint)AkCallbackType.AK_EndOfEvent, OnCheckEnded, null);
        }
        GUI.enabled = wasEnabled;
        if (checkBank == null) GUILayout.Label("测试音库未加载");
        if (checkId != 0)
        {
            int index = Mathf.Clamp((int)(Time.realtimeSinceStartup - checkStarted), 0, CheckChannels.Length - 1);
            GUILayout.Label($"测试 {index + 1}/{CheckChannels.Length}   {CheckChannels[index]}");
        }
        return checkId == 0;
    }

    private void Refresh()
    {
        ids.Clear();
        names.Clear();
        uint count = 0;
        var result = GetDevices(0, SystemSink, ref count, IntPtr.Zero);
        if (result == AKRESULT.AK_Success && count > 0)
        {
            int capacity = checked((int)count);
            using (var devices = new AkDeviceDescriptionArray(capacity))
            {
                result = GetDevices(0, SystemSink, ref count, devices.GetBuffer());
                if (result == AKRESULT.AK_Success)
                    for (int i = 0; i < capacity && i < count; i++)
                    {
                        var device = devices[i];
                        if ((device.deviceStateMask & AkAudioDeviceState.AkDeviceState_Active) == 0) continue;
                        ids.Add(device.idDevice);
                        names.Add(device.deviceName + (device.isDefaultDevice ? "（当前系统默认）" : ""));
                    }
            }
        }
        message = result != AKRESULT.AK_Success ? $"设备读取失败：{result}" :
            ids.Count == 0 ? "未枚举到可用设备" : null;
    }

    private void Select(uint id, string label)
    {
        using (var config = AkChannelConfig.Standard(AkUnitySoundEngine.AK_SPEAKER_SETUP_DOLBY_7_1_4))
        using (var settings = new AkOutputSettings("System", id, config))
        {
            var result = AkUnitySoundEngine.ReplaceOutput(settings, 0, out ulong requested);
            if (result != AKRESULT.AK_Success)
            {
                message = $"切换失败：{result}";
                return;
            }
            outputId = requested;
            selection = label;
            expanded = false;
            status = "等待设备响应…";
            message = null;
            nextQuery = Time.unscaledTime + 0.3f;
        }
    }

    private void DrawDevicePicker(bool canSwitch)
    {
        if (Time.unscaledTime >= nextQuery && AkUnitySoundEngine.IsInitialized())
        {
            nextQuery = Time.unscaledTime + 1f;
            using (var config = new AkChannelConfig())
            using (var caps = new Ak3DAudioSinkCapabilities())
            {
                var result = AkUnitySoundEngine.GetOutputDeviceConfiguration(outputId, config, caps);
                status = result == AKRESULT.AK_Success
                    ? $"{config.uNumChannels} ch   mask 0x{config.uChannelMask:X}   (7.1.4 = 0x2D63F)"
                    : $"设备未就绪：{result}";
            }
        }
        GUILayout.Label($"Wwise output: {status}");
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && canSwitch && AkUnitySoundEngine.IsInitialized();
        if (GUILayout.Button($"选择输出设备 · {selection}"))
        {
            expanded = !expanded;
            if (expanded) Refresh();
        }
        if (expanded)
        {
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(110));
            if (GUILayout.Button("使用系统默认设备")) Select(0, "系统默认");
            for (int i = 0; i < ids.Count; i++)
                if (GUILayout.Button(names[i])) Select(ids[i], names[i]);
            GUILayout.EndScrollView();
            if (GUILayout.Button("刷新设备列表")) Refresh();
        }
        if (!string.IsNullOrEmpty(message)) GUILayout.Label(message);
        GUI.enabled = enabled;
    }
}
#endif

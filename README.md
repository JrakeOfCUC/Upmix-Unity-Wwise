# Unity + Wwise 立体声空间化上混

将立体声素材拆成左右两个单声道声部，再由 Wwise 作为两个独立的 3D 声源播放。声源跟随监听者移动，但保持可配置的左右间距，因此原有立体声内容可以随场景获得空间位置和距离衰减。

此方案保留左右声道，不会从立体声中凭空生成离散的环绕声内容；空间感来自两个声道在场景中的独立定位，以及 Wwise 配置的空间化和衰减。

## 信号与位置

输入为左、右声道 `L`、`R`。制作素材时分别导出为等长、同采样率的单声道文件。Wwise 中分别为两个文件建立播放事件。Unity 将两个事件发布到不同的发声对象，并把对象放在监听者局部坐标的 `leftOffset` 和 `rightOffset` 位置：

```text
leftEvent  -> 左声道单声道素材 -> 左侧 3D 发声对象
rightEvent -> 右声道单声道素材 -> 右侧 3D 发声对象
```

不要直接将同一个立体声文件分别绑定到两个事件，否则会重复播放完整立体声，而不是分离左右声道。

## Unity / Wwise 设置

1. 在 Wwise 工程中导入左右单声道素材，分别创建事件（默认示例名为 `Play_Stereo_Left` 和 `Play_Stereo_Right`），并生成 SoundBanks。
2. 在 Unity 中安装并配置 Wwise Integration，将两个 SoundBank 加入项目。
3. 创建一个控制对象，添加 `StereoWwiseSpatializer`，并指定 Wwise Listener 的 Transform、左右发声对象及事件名。
4. 确保两个发声对象都带有 Wwise `AkGameObj`，并在 Wwise 声音对象中启用合适的 3D Positioning、Spatialization 和 Attenuation。
5. 从游戏逻辑或 UI 调用控制组件的 `Play()`。左右事件会同时发布；素材长度应相同。

`leftOffset` / `rightOffset` 是监听者局部坐标，默认左右各 0.5 米、前方 1 米。根据内容和 Wwise 衰减曲线调整间距；过大的间距可能造成声像过宽或中心声像变弱。

## 限制

- 这是基于两个声道的实时空间化，不是声道扩展、混响合成或对象音频编码。
- 使用前应确认素材已正确拆分为单声道，并确保两条 Wwise 事件的播放设置一致。
- 示例依赖已安装的 Wwise Unity Integration（`AkUnitySoundEngine`）；仓库不包含 Unity 工程或 Wwise SDK。

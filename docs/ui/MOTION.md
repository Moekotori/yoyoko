# 轻量动画系统

`Chat.Motion` 是独立的 Avalonia 表现层模块，只有现有 Avalonia 依赖，不引用 Core、网络、存储或业务 DTO。UI 引用 Motion；服务端与协议不变。

## 调用

在窗口或子树绑定一次减少动效偏好：

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:motion="clr-namespace:Chat.Motion;assembly=Chat.Motion"
        motion:Motion.ReduceMotion="{Binding ReduceMotion}">
  <motion:MotionHost Trigger="{Binding SelectedChannel}" Preset="Fade">
    <!-- 放现有页面控件，不复制页面或消息列表。 -->
  </motion:MotionHost>
</Window>
```

`MotionHost` 接受一个 Child。进入可见树、从隐藏恢复、Child 替换以及 Trigger 值变化都会触发。相同 Trigger 不重播；需要主动重播时，在 UI 线程调用 `host.Play()`，调用 `host.Stop()` 立即取消并显示最终状态。首次布局不等待动画完成，业务操作也不等待动画。

频道分组与社区菜单折叠使用 `RevealHost`：

```xml
<motion:RevealHost IsOpen="{Binding TextExpanded}">
  <ItemsControl ItemsSource="{Binding TextChannels}" />
</motion:RevealHost>
```

`IsOpen` 变化时从当前进度接到目标，箭头旋转由 `groupChevron` / `menuChevron` 样式承担。首次进入可见树只对齐开合状态，不播放入场。`Stop()` 立即落到当前 `IsOpen`。减少动效、隐藏或最小化会取消并对齐。

| 参数 | 默认值与含义 |
| --- | --- |
| `Preset` | `Enter`：淡入 + 从下方 8px 归位 |
| `Fade` | 只淡入；频道消息使用轻微透明度变化 |
| `SlideLeft` / `SlideRight` | 从右 / 左侧 8px 淡入归位；方向由调用方选择 |
| `SlideUp` / `SlideDown` | 从下 / 上方 8px 淡入归位 |
| `None` | 立即呈现 |
| `StartOpacity` | 入场起始透明度，默认 0；频道消息为 0.82，避免切换闪空 |
| `Duration` | `MotionHost` 默认 `MotionTokens.Page`（160ms）；`RevealHost` 默认 `MotionTokens.Reveal`（220ms）；允许 0–500ms，0 表示立即呈现 |
| `Motion.ReduceMotion` | 可继承，true 时取消当前与待执行动画；关闭该偏好本身不触发动画 |
| `IsRunning` | 只读运行状态，供诊断；不是用于绑定的通知属性 |

控件反馈可复用 `MotionTokens.Feedback`（120ms）。`MotionHost` 自己的 Opacity 和 RenderTransform 由模块持有；自定义透明度、缩放、旋转放到 Child，避免两个动画源同时写同一属性。不要给 MotionHost 额外设置这两个属性的样式动画或 Transition。

消息时间线滚轮使用 `SmoothScroll`：在 160ms 内用 CubicEaseOut 接到目标偏移，连续滚轮只改目标、不排队。减少动效、程序跳转和拉历史后的位置恢复立即落点。没有常驻帧循环。

## 生命周期与开销

- 使用 Avalonia 自带动画时钟和 CubicEaseOut；无自建定时器、后台线程、常驻帧循环、全局对象列表或循环动画。
- 页面切换只插值透明度和渲染位移，不逐帧修改 Width、Height、Margin，不复制页面、不截图缓存旧页、不保留历史视图。
- `RevealHost` 是折叠专用：子节点保持完整尺寸并用裁剪显隐，宿主通过 Measure 报告插值高度，让下方分组跟着移动。不写 Width/Height 属性，不复制子树。
- 每个容器最多一个运行中的动画、一个 Dispatcher 待执行请求。同一 UI 轮的请求合并；后续请求从当前可见值接续，取消旧动画，不排队播放。
- 完成或取消会移除动画属性值，回到透明度 1、位移 0；旧任务的完成回调不能清除新动画的所有权。每次运行的 CancellationTokenSource 在完成时释放。
- 自身/祖先隐藏、窗口最小化、离开可见树及减少动效会取消动画。卸载时解除祖先属性订阅。最小化恢复不回放过期请求。
- 只在页面/面板边界使用 `MotionHost`，避免为每条消息创建动画容器。他人新到达的一行用 `ItemEnter.Play` 做一次 120ms 淡入+上移（起始透明度 0.55、位移 6px），播放后不再占宿主；自己发送、历史加载和频道切换不播。容器随可见树保留少量状态；没有承诺零分配或 GPU 专用线程执行。

当前是有界的入场与切换动效基础。退场等待、旧新页交叉淡化、弹簧/物理动画、跨元素共享过渡、系统级减少动态效果自动检测均 **Not implemented yet**。应用已有减少动效偏好已接入。

## 当前接入与验证

已接入添加实例、认证、文字/语音频道内容、设置入场、设计预览频道切换，以及新消息 `ItemEnter`；Theme 的按钮反馈使用共用 120ms token。设置分类切换不再位移或淡入，避免侧栏点选时整页抖动；设置控件自身的微交互继续由 UI 样式负责。文字/语音频道分组与社区菜单分区使用 `RevealHost` 做可中断收展。进入设置时壳层频道列由 `ShellMotion` 做 160ms 收起，设置页用 `SlideLeft` 入场。消息时间线滚轮走 `SmoothScroll`。

2026-09-21：

- Release 桌面构建：0 警告、0 错误；客户端模块边界检查通过。
- `dotnet run --project tests/motion -c Release`：使用 Avalonia Headless 的实际 Dispatcher/动画时钟，验证中间插值、100 次连续 Trigger、不中断布局/Child transform、减少动效、祖先隐藏、最小化、排队取消、卸载/重新挂载与各预设归位；`RevealHost` 另验首次不对齐开合状态、收展中间高度、下方分组跟随、中途反向与减少动效对齐。Headless 只作为测试依赖，不进入桌面产物。
- 生产 `SettingsView` 的 Skia 离屏渲染：验证中间帧、连续分类切换及真实 ReduceMotion 绑定；已检查 [中间帧](motion/appearance-moving.png) 和 [结束帧](motion/appearance-settled.png)。
- 已启动隔离缓存的 macOS Release 客户端，但本任务的原生窗口读取超时，因此未验收原生输入及帧节奏；Windows/Linux 未运行。

空闲后无本模块运行任务/待播放队列由聚焦检查与代码路径确认。尚未做同条件的整进程 RAM/CPU 前后对比，不把模块结构或约 12 KiB 的 Release DLL 文件大小当作运行时内存结果，也不声称应用已达到 50–100 MB 目标。

实现参考 Avalonia 官方 [RunAsync / keyframe 生命周期](https://docs.avaloniaui.net/docs/graphics-animation/keyframe-animations) 和 [渲染变换](https://docs.avaloniaui.net/docs/graphics-animation/transforms)。

### 频道切换优化（2026-09-21）

文字频道只在第一份缓存消息到位时触发消息区 120ms、0.82 → 1 的淡入，标题、输入框和成员区不再跟随整页从透明重播。网络刷新不重复触发动效，等待缓存期间不提前显示空频道文案。

切换会取消上个频道的历史加载，过期的缓存/网络回调不能更新或滚动新频道。首次加载不再在网络完成后强制拉回底部，翻页回调也检查频道归属。消息刷新与频道导航刷新分开，避免重新创建导航按钮；相同成员列表保留原容器。缓存刷新会更新已有消息行持有的 TimelineItem，避免继续显示旧快照。频道加载所有权单独放在 ShellViewModel.ChannelNavigation.cs。

验证：UI Release 构建和模块边界检查通过；动效聚焦检查包含 0.82 起始透明度、连续触发不中断至透明以及既有减少动效/隐藏/最小化清理。原生窗口结果见下方记录；尚未测量逐帧耗时、整进程性能或 Windows/Linux。

原生验证：macOS Release，使用隔离缓存连接真实的本地 HTTP/Gateway 实例，两个文字频道各三条测试消息；完成连续往返切换，最终标题、消息、导航选中项一致，输入框保留在固定位置。将滚动请求合并到 Loaded 优先级后，切换回短列表的首条消息/作者头完整显示，不再沿用布局前的错误位置。验证窗口与原有用户窗口独立。最终完整桌面 Release 构建 0 警告、0 错误；此记录不代表高消息负载或逐帧性能验收。

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

| 参数 | 默认值与含义 |
| --- | --- |
| `Preset` | `Enter`：淡入 + 从下方 8px 归位 |
| `Fade` | 只淡入，适合频道内容 |
| `SlideLeft` / `SlideRight` | 从右 / 左侧 8px 淡入归位；方向由调用方选择 |
| `None` | 立即呈现 |
| `Duration` | `MotionTokens.Page`，160ms；允许 0–500ms，0 表示立即呈现 |
| `Motion.ReduceMotion` | 可继承，true 时取消当前与待执行动画；关闭该偏好本身不触发动画 |
| `IsRunning` | 只读运行状态，供诊断；不是用于绑定的通知属性 |

控件反馈可复用 `MotionTokens.Feedback`（120ms）。`MotionHost` 自己的 Opacity 和 RenderTransform 由模块持有；自定义透明度、缩放、旋转放到 Child，避免两个动画源同时写同一属性。不要给 MotionHost 额外设置这两个属性的样式动画或 Transition。

## 生命周期与开销

- 使用 Avalonia 自带动画时钟和 CubicEaseOut；无自建定时器、后台线程、常驻帧循环、全局对象列表或循环动画。
- 只插值透明度和渲染位移，不逐帧修改 Width、Height、Margin，不复制页面、不截图缓存旧页、不保留历史视图。
- 每个容器最多一个运行中的动画、一个 Dispatcher 待执行请求。同一 UI 轮的请求合并；后续请求从当前可见值接续，取消旧动画，不排队播放。
- 完成或取消会移除动画属性值，回到透明度 1、位移 0；旧任务的完成回调不能清除新动画的所有权。每次运行的 CancellationTokenSource 在完成时释放。
- 自身/祖先隐藏、窗口最小化、离开可见树及减少动效会取消动画。卸载时解除祖先属性订阅。最小化恢复不回放过期请求。
- 只在页面/面板边界使用，避免为每条消息创建动画容器。容器随可见树保留少量状态；没有承诺零分配或 GPU 专用线程执行。

当前是有界的入场与切换动效基础。退场等待、旧新页交叉淡化、弹簧/物理动画、跨元素共享过渡、系统级减少动态效果自动检测均 **Not implemented yet**。应用已有减少动效偏好已接入。

## 当前接入与验证

已接入添加实例、认证、文字/语音频道内容、设置分类和设计预览频道切换；Theme 的按钮反馈使用共用 120ms token。设置控件自身的微交互继续由 UI 样式负责，不与页面 MotionHost 叠加同一组属性。

2026-09-21：

- Release 桌面构建：0 警告、0 错误；客户端模块边界检查通过。
- `dotnet run --project tests/motion -c Release`：使用 Avalonia Headless 的实际 Dispatcher/动画时钟，验证中间插值、100 次连续 Trigger、不中断布局/Child transform、减少动效、祖先隐藏、最小化、排队取消、卸载/重新挂载与各预设归位。Headless 只作为测试依赖，不进入桌面产物。
- 生产 `SettingsView` 的 Skia 离屏渲染：验证中间帧、连续分类切换及真实 ReduceMotion 绑定；已检查 [中间帧](motion/appearance-moving.png) 和 [结束帧](motion/appearance-settled.png)。
- 已启动隔离缓存的 macOS Release 客户端，但本任务的原生窗口读取超时，因此未验收原生输入及帧节奏；Windows/Linux 未运行。

空闲后无本模块运行任务/待播放队列由聚焦检查与代码路径确认。尚未做同条件的整进程 RAM/CPU 前后对比，不把模块结构或约 12 KiB 的 Release DLL 文件大小当作运行时内存结果，也不声称应用已达到 50–100 MB 目标。

实现参考 Avalonia 官方 [RunAsync / keyframe 生命周期](https://docs.avaloniaui.net/docs/graphics-animation/keyframe-animations) 和 [渲染变换](https://docs.avaloniaui.net/docs/graphics-animation/transforms)。

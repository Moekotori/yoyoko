# ADR 0005 — 内存预算与简洁 UI

Status: Accepted · 2026-09-20

## Context

降低 idle 内存、启动等待和大量消息造成的 UI/GC 压力，遵循用户减少圆角与边框的要求。

## Decision

轻量 MVVM、编译绑定、分页虚拟化、cache byte budgets、按需媒体；UI 平面简洁。

最多 300 条时间线模型，单页 50/100，缩略图优先，有界网络帧和缓存；无伪造数据和装饰卡片。

## Consequences

50–100 MiB 是目标不是证明。平台渲染、运行时、字体和媒体需真实采样；性能记录必须注明构建、窗口、进程范围和场景。

## 2026-09-21：界面资源预算落实

窗口驻留采用前台、空闲、压力、隐藏四档；将预算交给各图片所有者，隐藏时暂停可重建 UI 工作，保留会话同步与语音。以回收真实资源控制占用，不强制 GC 或清空系统 working set。借鉴 ECHO UltraLight 的生命周期策略，不引入 Electron Renderer 卸载或重启进程。[实现、阈值和验证边界](../MEMORY_SCHEDULING.md)。

### 可选深度驻留

用户启用 UltraLight 后，隐藏/最小化释放可重建 ShellSurface，原生窗口和 Shell/实例会话继续存活。恢复不重新鉴权、不重连 Gateway/语音、不重新选择设备，只有真正退出应用才走原有 Dispose 链。开关默认关闭并本地保存；不得将后台 UI 卸载调用接到 VoiceRuntime.Leave 或进程退出上。

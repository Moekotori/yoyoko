# Performance baseline

Phase 0 的测量工具保持小而可复现，不创建性能仪表盘或无真实功能的压测。

```sh
dotnet build Chat.sln -c Release
# 启动 App 后取得实际客户端 PID（不是 dotnet build 的 PID）
python3 scripts/measure_idle.py <PID> --seconds 10
```

记录 OS/CPU、SDK、Avalonia 版本、Release/Debug、窗口尺寸/可见性、进程数、数据库条数、首次/热启动。`ps RSS` 不等同于平台 private working set，CPU 是 ps 生命周期平均值，不能冒充精确采样区间 CPU；跨平台比较应使用等价工具。

| 指标 | Phase 0 | 后续方法 |
| --- | --- | --- |
| Idle RAM / CPU | 实际窗口进程 RSS / ps CPU 可测 | 50–100 MiB 为目标，媒体 worker 合并统计 |
| 启动时间 | 未来增 first-painted frame 标记 | 至可交互首帧，不用端口打开时间替代 |
| 100/1000/10000 messages | 未实现真实时间线，不给虚假曲线 | 同频道相同消息，记录窗口模型数、RSS、GC |
| message render latency | Not implemented yet | event commit → rendered frame p50/p95 |
| channel switch latency | Not implemented yet | input → first cache page paint p50/p95 |
| WS reconnect | 只有 backoff policy | disconnect → resumed committed event |
| voice CPU / share CPU | Not implemented yet | 整棵进程树、同编码分辨率/设备 |

第一次有真实聊天数据后再加负载 benchmark；不要把创建 10,000 个字符串当成消息渲染结果。

2026-09-21：已有真实时间线和四档图片资源调度，旧表中的 Phase 0 内容仅描述历史基线。当前聚焦回归、独立窗口样本和未测负载见 [内存调度记录](../docs/MEMORY_SCHEDULING.md)。

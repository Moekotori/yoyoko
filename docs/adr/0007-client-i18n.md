# ADR 0007 — 客户端界面语言

Status: Accepted · 2026-09-20

## Context

桌面客户端需要中文、英文和日语，并允许用户在设置中切换。语言是客户端级偏好，不是实例身份，也不能写进 SQLite 消息缓存。

## Decision

- 独立 `Chat.Localization` 项目持有 `Locale`、`ILocalePreference`、`ITextCatalog` 和三语文案目录；不依赖 Avalonia、UI、网络或存储。
- 每条文案只写一行（key / 英 / 中 / 日）。XAML `{i18n:T Key}` 随语言切换刷新。C# 用 `I18n.T(TextKey.X)` 或注入的 `I18n.Get`。缺键回退到英文，再缺则显示 key。
- App 实现 `FileLocalePreference`，组合 `TextCatalog` 与 `I18n`。UI 只通过 `I18n.Get` / `{i18n:T Key}` 取文案。
- Core 与 Networking 抛 `ClientFault(key)`，不查文案表；Shell 在展示层翻译。
- 内置 `zh-Hans`、`en`、`ja`。用户可导入 JSON 语言包新增语言或覆盖内置文案；设置页支持拖拽 `.json`。
- 热路径把当前语言合并成一张 FrozenDictionary，无参数查找一次哈希。
- 用户选择写入 `preferences.json`。语言包在 `locales/<code>.json`。`CHAT_LOCALE` 仅在尚无已保存选择时生效。切换后立即更新，不要求重启。

## Consequences

新增界面字符串必须同时写入三种语言目录。协议字段、用户内容、社区/频道名不翻译。Core 不再持有 UI 文案或静态当前语言。

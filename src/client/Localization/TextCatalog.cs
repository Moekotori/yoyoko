using System.Collections.Frozen;
using System.Globalization;

namespace Chat.Localization;

public sealed class TextCatalog : ITextCatalog
{
    private readonly ILanguagePacks? _packs;
    private Locale _hotLocale;
    private FrozenDictionary<string, string>? _hot;
    public TextCatalog(ILanguagePacks? packs = null)
    {
        _packs = packs;
        if (packs is not null) packs.Changed += () => { _hot = null; Changed?.Invoke(); };
    }

    public event Action? Changed;
    public IReadOnlyList<Locale> Available => _packs?.Available ?? Locale.Supported;
    public IReadOnlyCollection<string> Keys => English.Keys;

    public string Get(Locale locale, string key)
    {
        var table = Hot(locale);
        return table.TryGetValue(key, out var value) ? value : key;
    }

    public string Get(Locale locale, string key, params object[] args)
    {
        var template = Get(locale, key);
        return args.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, args);
    }

    public bool SameKeys() =>
        Keys.Count == Chinese.Count && Keys.Count == Japanese.Count &&
        Keys.All(Chinese.ContainsKey) && Keys.All(Japanese.ContainsKey);

    private FrozenDictionary<string, string> Hot(Locale locale)
    {
        if (_hot is not null && _hotLocale == locale) return _hot;
        _hot = Merge(locale);
        _hotLocale = locale;
        return _hot;
    }

    private FrozenDictionary<string, string> Merge(Locale locale)
    {
        var builtIn = Table(locale);
        var pack = _packs?.Find(locale.Code);
        if (pack is null && ReferenceEquals(builtIn, English)) return English;
        if (pack is null) return Merge(English, builtIn);
        var table = new Dictionary<string, string>(English.Count, StringComparer.Ordinal);
        foreach (var pair in English) table[pair.Key] = pair.Value;
        if (pack.Fallback is { } fallback && !fallback.Equals(locale.Code, StringComparison.OrdinalIgnoreCase))
            foreach (var pair in Table(Locale.Parse(fallback))) table[pair.Key] = pair.Value;
        if (!ReferenceEquals(builtIn, English))
            foreach (var pair in builtIn) table[pair.Key] = pair.Value;
        foreach (var pair in pack.Strings)
            if (English.ContainsKey(pair.Key)) table[pair.Key] = pair.Value;
        return table.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenDictionary<string, string> Merge(FrozenDictionary<string, string> fallback, FrozenDictionary<string, string> overlay)
    {
        var table = new Dictionary<string, string>(fallback.Count, StringComparer.Ordinal);
        foreach (var pair in fallback) table[pair.Key] = pair.Value;
        foreach (var pair in overlay) table[pair.Key] = pair.Value;
        return table.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenDictionary<string, string> Table(Locale locale) => locale.Code switch
    {
        "zh-Hans" => Chinese,
        "ja" => Japanese,
        _ => English
    };

    private static FrozenDictionary<string, string> Map(IEnumerable<(string Key, string En, string Zh, string Ja)> rows, Func<(string Key, string En, string Zh, string Ja), string> pick) =>
        rows.ToFrozenDictionary(row => row.Key, pick, StringComparer.Ordinal);

    // One row per string: key, English, Chinese, Japanese. Add new copy here only.
    private static readonly (string Key, string En, string Zh, string Ja)[] Rows =
    [
        (TextKey.UltraLightRestoreFailed, "Interface recovery failed. Background sessions are retained.", "界面恢复失败，后台会话仍保留。", "画面の復元に失敗しました。バックグラウンドのセッションは維持されています。"),
        (TextKey.UltraLightRestoreRetry, "Retry interface recovery", "重试恢复界面", "画面の復元を再試行"),
        (TextKey.UltraLightMode, "UltraLight", "UltraLight", "UltraLight"),
        (TextKey.UltraLightHelp, "Unload the interface when minimized or hidden; keep chats and voice connected.", "最小化或隐藏时卸载界面，保持消息与语音连接。", "最小化・非表示時に画面を解放し、チャットと音声の接続を維持します。"),
        ("RenameChannel", "Rename channel", "重命名频道", "チャンネル名を変更"),
        ("DeleteChannel", "Delete channel", "删除频道", "チャンネルを削除"),
        ("DeleteChannelConfirm", "Delete “{0}”? Its messages will be permanently deleted.", "删除「{0}」？频道内的消息将被永久删除。", "「{0}」を削除しますか？メッセージも完全に削除されます。"),
        ("ChannelNameInvalid", "Enter a channel name (1–100 UTF-8 bytes).", "请输入频道名称（1–100 UTF-8 字节）。", "チャンネル名を入力してください（1〜100 UTF-8 バイト）。"),
        ("OpenChannel", "Open channel", "打开频道", "チャンネルを開く"),
        ("JoinVoiceChannel", "Join voice", "加入语音", "ボイスに参加"),
        ("VoiceAudioOff", "Audio not connected", "音频未接通", "音声未接続"),
        ("ChannelSave", "Save", "保存", "保存"),
        ("ChannelCancel", "Cancel", "取消", "キャンセル"),
        ("CreateTextChannel", "Create text channel", "添加文字频道", "テキストチャンネルを追加"),
        ("CreateVoiceChannel", "Create voice channel", "添加语音频道", "ボイスチャンネルを追加"),
        ("ServerConnection", "Server connection", "服务器", "サーバー"),
        ("ConnectServer", "Connect", "连接", "接続"),
        ("ConnectingServer", "Connecting…", "正在连接…", "接続中…"),
        ("ConnectedServer", "Connected", "已连接", "接続済み"),
        ("DisconnectServer", "Disconnect", "断开", "切断"),
        ("DisconnectingServer", "Disconnecting…", "正在断开…", "切断中…"),
        ("ConnectionState", "Status", "状态", "状態"),
        ("ServerName", "Server name", "服务器名称", "サーバー名"),
        ("ServerLatency", "Latency", "延迟", "遅延"),
        ("LatencyMs", "{0} ms", "{0} ms", "{0} ms"),
        ("LatencyMeasuring", "Measuring…", "测量中…", "測定中…"),
        ("LatencyUnreachable", "Unreachable", "不可达", "応答なし"),
        ("ServerProtocol", "Protocol", "协议", "プロトコル"),
        ("ProtocolDetail", "Protocol {0} · API {1}", "协议 {0} · API {1}", "プロトコル {0} · API {1}"),
        ("ServerHost", "Host", "主机", "ホスト"),
        ("Community", "Community", "社区", "コミュニティ"),
        ("ServerAddressOrInvite", "Address or invite", "服务器地址或邀请码", "アドレスまたは招待コード"),
        ("InvalidInvite", "Paste a server invite link, or a server address.", "请粘贴服务器邀请链接，或输入服务器地址。", "サーバーの招待リンクまたはアドレスを貼り付けてください。"),
        ("CopyInvite", "Copy invite", "复制邀请码", "招待コードをコピー"),
        ("SessionRecoveryRequired", "This server already has a saved account. Sign in again.", "此服务器已有保存的账号，请重新登录。", "このサーバーには保存済みのアカウントがあります。再ログインしてください。"),
        ("PageParticipants", "In this conversation", "本页参与者", "この会話の参加者"),
        ("Settings", "Settings", "设置", "設定"),
        ("Language", "Language", "语言", "言語"),
        ("CloseProfile", "Close profile", "关闭个人资料", "プロフィールを閉じる"),
        ("AudioDevices", "Audio devices", "音频设备", "オーディオデバイス"),
        ("CloseSettings", "Close settings", "关闭设置", "設定を閉じる"),
        ("AddInstance", "Add server", "添加服务器", "サーバーを追加"),
        ("Instances", "Servers", "服务器列表", "サーバー"),
        ("ConnectCommunity", "Connect to your community", "连接你的社区", "コミュニティに接続"),
        ("InstanceAddress", "Server address", "服务器地址", "サーバーアドレス"),
        ("InstanceAddressPlaceholder", "chat.example.com", "chat.example.com", "chat.example.com"),
        ("ActionStatus", "Status", "操作状态", "状態"),
        ("SignIn", "Sign in", "登录", "ログイン"),
        ("Register", "Sign up", "注册", "登録"),
        ("Username", "Username", "用户名", "ユーザー名"),
        ("DisplayName", "Display name (sign up)", "显示名（注册）", "表示名（登録）"),
        ("DisplayNameField", "Display name", "显示名", "表示名"),
        ("Password", "Password", "密码", "パスワード"),
        ("Channels", "Channels", "频道", "チャンネル"),
        ("ChannelList", "Channel list", "频道列表", "チャンネル一覧"),
        ("NewCommunity", "New community name", "新社区名称", "新しいコミュニティ名"),
        ("CreateCommunity", "Create community", "创建社区", "コミュニティを作成"),
        ("CreateChannel", "Create channel", "创建频道", "チャンネルを作成"),
        ("NewChannelName", "Channel name", "频道名称", "チャンネル名"),
        ("VoiceChannel", "Voice channel", "语音频道", "ボイスチャンネル"),
        ("InviteCode", "Invite code", "邀请码", "招待コード"),
        ("JoinCommunity", "Join community", "加入社区", "コミュニティに参加"),
        ("SignOut", "Sign out", "退出账号", "ログアウト"),
        ("SignedOut", "Signed out", "未登录", "未ログイン"),
        ("Workspace", "Workspace", "工作空间", "ワークスペース"),
        ("NotConnected", "No server connected", "尚未连接服务器", "サーバー未接続"),
        ("Voice", "Voice", "语音", "ボイス"),
        ("InviteHintEmpty", "Create or join a community to start chatting.", "创建或加入一个社区后即可聊天。", "コミュニティを作成または参加するとチャットできます。"),
        ("InviteHint", "Invite {0}", "邀请码 {0}", "招待コード {0}"),
        ("Messages", "Messages", "消息", "メッセージ"),
        ("Sending", "Sending", "发送中", "送信中"),
        ("SendFailed", "Couldn’t send", "发送失败", "送信に失敗しました"),
        ("RetrySend", "Retry", "重试", "再試行"),
        ("Image", "Image", "图片", "画像"),
        ("SendMessage", "Send a message", "发送消息", "メッセージを送信"),
        ("Send", "Send", "发送", "送信"),
        ("MessageInput", "Message", "消息输入", "メッセージ入力"),
        ("PickImages", "Send images", "发送图片", "画像を送信"),
        ("LeaveVoice", "Leave", "离开", "退出"),
        ("Mute", "Mute", "静音", "ミュート"),
        ("Unmute", "Unmute", "取消静音", "ミュート解除"),
        ("Deafen", "Deafen", "耳聋", "スピーカーミュート"),
        ("Undeafen", "Undeafen", "取消耳聋", "スピーカーミュート解除"),
        ("VoiceConnected", "Voice connected", "语音已连接", "ボイスに接続しました"),
        ("VoiceJoinHint", "Double-click a voice channel to join", "双击语音频道加入", "ボイスチャンネルをダブルクリックして参加"),
        ("VoiceMediaError", "In the channel, audio not connected: {0}", "已进入频道，音频未接通：{0}", "チャンネルに入りましたが、音声は未接続です：{0}"),
        ("AudioQuality", "Audio quality", "音质", "音質"),
        ("ChannelQualityCap", "Channel maximum", "频道上限", "チャンネル上限"),
        ("QualityStandard", "Standard", "标准", "標準"),
        ("QualityStandardDetail", "48 kHz mono · 64 kbps", "48 kHz 单声道 · 64 kbps", "48 kHz モノラル · 64 kbps"),
        ("QualityHigh", "High", "高", "高"),
        ("QualityHighDetail", "48 kHz stereo · 128 kbps", "48 kHz 立体声 · 128 kbps", "48 kHz ステレオ · 128 kbps"),
        ("QualityVeryHigh", "Very high", "很高", "非常に高い"),
        ("QualityVeryHighDetail", "48 kHz stereo · 384 kbps", "48 kHz 立体声 · 384 kbps", "48 kHz ステレオ · 384 kbps"),
        ("QualityStudio", "Studio", "最高", "最高"),
        ("QualityStudioDetail", "48 kHz stereo · 510 kbps · Opus maximum", "48 kHz 立体声 · 510 kbps · Opus 上限", "48 kHz ステレオ · 510 kbps · Opus 最大"),
        ("InputDevice", "Input device", "输入设备", "入力デバイス"),
        ("OutputDevice", "Output device", "输出设备", "出力デバイス"),
        ("DefaultDevice", "System default", "系统默认", "システムのデフォルト"),
        ("DevicesUnavailable", "Couldn’t list audio devices: {0}", "无法列出音频设备：{0}", "オーディオデバイスを列挙できません：{0}"),
        ("CheckDevices", "Check devices", "检查设备", "デバイスを確認"),
        ("StopDeviceCheck", "Stop check", "停止检查", "確認を停止"),
        (TextKey.HeadsetMediaKeys, "Headset media keys", "耳机媒体键", "ヘッドセットのメディアキー"),
        (TextKey.HeadsetMediaKeysHelp, "While in a voice channel, headset play/pause mutes you. Other apps may lose the system media card until you leave.", "在语音频道内，耳机播放键切换静音。离开前可能会占用系统正在播放卡片。", "ボイスチャンネル中、ヘッドセットの再生キーでミュートします。退出するまで他アプリの再生カードが隠れる場合があります。"),
        ("MutedSuffix", " · muted", " · 静音", " · ミュート"),
        ("DeafenedSuffix", " · deafened", " · 耳聋", " · スピーカーミュート"),
        ("CacheLoadFailed", "Couldn’t load local cache: {0}", "本地缓存加载失败：{0}", "ローカルキャッシュを読み込めませんでした：{0}"),
        ("Discovering", "Finding server…", "正在连接服务器…", "サーバーを検出しています…"),
        ("InstanceSaved", "Server saved. Sign in or sign up.", "已连接，请登录或注册。", "サーバーを保存しました。ログインまたは登録してください。"),
        ("NeedInstance", "Add a server first.", "请先添加服务器。", "先にサーバーを追加してください。"),
        ("Registering", "Signing up…", "正在注册…", "登録しています…"),
        ("SigningIn", "Signing in…", "正在登录…", "ログインしています…"),
        ("NeedSignIn", "Sign in first.", "请先登录。", "先にログインしてください。"),
        ("NoInstanceSelected", "No server selected.", "没有选中的服务器。", "サーバーが選択されていません。"),
        ("Cancelled", "Cancelled.", "已取消。", "キャンセルしました。"),
        ("ImagesSelected", "{0} image(s) selected", "已选择 {0} 张图片", "{0} 枚の画像を選択しました"),
        ("ProtocolIncompatible", "This server uses an incompatible protocol version.", "服务器协议版本不兼容。", "このサーバーのプロトコルバージョンは互換性がありません。"),
        ("InstanceAddressConflict", "This server was already added from a different address.", "此服务器已通过另一个地址添加。", "このサーバーは別のアドレスで追加済みです。"),
        ("TooManyInstances", "You can add up to 32 servers.", "最多可添加 32 个服务器。", "サーバーは最大 32 件まで追加できます。"),
        ("InvalidInstanceAddress", "Enter a server domain or HTTPS address. HTTP is allowed on this computer and LAN.", "请输入服务器域名或 HTTPS 地址；本机和局域网开发可使用 HTTP。", "サーバーのドメインまたは HTTPS アドレスを入力してください。このコンピュータと LAN では HTTP を利用できます。"),
        ("DiscoveryEmpty", "Server discovery returned an empty response.", "服务器发现响应为空。", "サーバー検出の応答が空です。"),
        ("DiscoveryIdentityInvalid", "Server identity is invalid.", "服务器身份无效。", "サーバーの識別情報が無効です。"),
        ("DiscoveryOriginMismatch", "API and Gateway must share the server origin.", "API 和 Gateway 必须与服务器同源。", "API と Gateway はサーバーと同じオリジンである必要があります。"),
        ("DiscoveryInsecureEndpoint", "Server endpoints must use a secure connection.", "服务器端点必须使用安全连接。", "サーバーのエンドポイントは安全な接続を使う必要があります。"),
        ("SelectChannel", "Select a channel", "选择一个频道", "チャンネルを選択"),
        ("MessageToChannel", "Message #{0}", "发消息到 #{0}", "#{0} に送信"),
        ("NoMatchingMessages", "No matching messages", "没有匹配的消息", "一致するメッセージはありません"),
        ("StillQuiet", "It’s quiet here", "这里还很安静", "まだ静かです"),
        ("DesignPreview", "Design preview", "设计预览", "デザインプレビュー"),
        ("Offline", "Offline", "离线", "オフライン"),
        ("NotSignedIn", "Not signed in", "尚未登录", "未ログイン"),
        ("Online", "Online", "在线", "オンライン"),
        ("OnlineCount", "Online — {0}", "在线 — {0}", "オンライン — {0}"),
        ("ClipboardUnavailable", "Clipboard unavailable", "剪贴板不可用", "クリップボードを利用できません"),
        ("ClipboardFailed", "Couldn’t copy to the clipboard", "无法复制到剪贴板", "クリップボードにコピーできませんでした"),
        ("WindowTitlePreview", "{0} · design preview", "{0} · 设计预览", "{0} · デザインプレビュー"),
        ("ChannelFallback", "Channel", "频道", "チャンネル"),
        ("NotImplemented", "Not implemented yet", "尚未实现", "未実装"),
        ("Mic", "Microphone", "麦克风", "マイク"),
        ("Headphones", "Headphones", "耳机", "ヘッドフォン"),
        ("MicUnavailable", "Microphone · Not implemented yet", "麦克风 · 尚未实现", "マイク · 未実装"),
        ("HeadphonesUnavailable", "Headphones · Not implemented yet", "耳机 · 尚未实现", "ヘッドフォン · 未実装"),
        ("SettingsUnavailable", "Settings · Not implemented yet", "设置 · 尚未实现", "設定 · 未実装"),
        ("TextChannels", "Text channels", "文字频道", "テキストチャンネル"),
        ("VoiceChannels", "Voice channels", "语音频道", "ボイスチャンネル"),
        ("ExpandTextChannels", "Expand or collapse text channels", "展开或收起文字频道", "テキストチャンネルを展開または折りたたむ"),
        ("ExpandVoiceChannels", "Expand or collapse voice channels", "展开或收起语音频道", "ボイスチャンネルを展開または折りたたむ"),
        ("JumpToChannel", "Jump to channel", "跳到频道", "チャンネルへ移動"),
        ("MembersShortcut", "Members · ⌘ ⇧ U / Ctrl Shift U", "成员 · ⌘ ⇧ U / Ctrl Shift U", "メンバー · ⌘ ⇧ U / Ctrl Shift U"),
        ("SettingsShortcut", "Settings · ⌘ , / Ctrl ,", "设置 · ⌘ , / Ctrl ,", "設定 · ⌘ , / Ctrl ,"),
        ("CloseTabShortcut", "Close tab · ⌘ W / Ctrl W", "关闭标签 · ⌘ W / Ctrl W", "タブを閉じる · ⌘ W / Ctrl W"),
        ("SendEnterTip", "Send · Enter", "发送 · Enter", "送信 · Enter"),
        ("SendChordTip", "Send · Ctrl / ⌘ Enter", "发送 · Ctrl / ⌘ Enter", "送信 · Ctrl / ⌘ Enter"),
        ("SearchMessages", "Search messages", "搜索消息", "メッセージを検索"),
        ("SearchShortcut", "Search messages · ⌘ F / Ctrl F", "搜索消息 · ⌘ F / Ctrl F", "メッセージを検索 · ⌘ F / Ctrl F"),
        ("Members", "Members", "成员", "メンバー"),
        ("ToggleMembers", "Toggle member list", "切换成员栏", "メンバー一覧を切り替え"),
        ("CloseSearch", "Close search", "关闭搜索", "検索を閉じる"),
        ("MessageTimeline", "Message timeline", "消息时间线", "メッセージタイムライン"),
        ("CopyText", "Copy text", "复制文本", "テキストをコピー"),
        ("CopyMessage", "Copy message", "复制消息", "メッセージをコピー"),
        ("ReactionUnavailable", "Reaction (not implemented yet)", "回应（尚未实现）", "リアクション（未実装）"),
        ("AddAttachment", "Add attachment", "添加附件", "添付を追加"),
        ("Emoji", "Emoji", "表情", "絵文字"),
        ("CloseTab", "Close tab", "关闭标签", "タブを閉じる"),
        ("CloseChannelTab", "Close channel tab", "关闭频道标签", "チャンネルタブを閉じる"),
        ("SearchInChannel", "Search this channel", "搜索此频道", "このチャンネルを検索"),
        ("SearchContent", "Search message content", "搜索消息内容", "メッセージ内容を検索"),
        ("ManageInstances", "Manage servers", "管理服务器", "サーバーを管理"),
        ("CloseAddInstance", "Close add server", "关闭添加服务器", "追加画面を閉じる"),
        ("CloseNotice", "Dismiss notice", "关闭提示", "通知を閉じる"),
        ("DropFiles", "Drop files here to send", "放到这里发送文件", "ここにドロップして送信"),
        ("DownloadLocal", "Download", "下载到本地", "ダウンロード"),
        ("DownloadFile", "Download file", "下载文件", "ファイルをダウンロード"),
        ("File", "File", "文件", "ファイル"),
        ("AddFile", "Add file", "添加文件", "ファイルを追加"),
        ("SendMessageOrDrop", "Send a message, or drop files", "发送消息，或拖入文件", "メッセージを送信、またはファイルをドロップ"),
        ("PickFiles", "Send files", "发送文件", "ファイルを送信"),
        ("SaveFile", "Save file", "保存文件", "ファイルを保存"),
        ("RemoveFile", "Remove", "移除", "削除"),
        ("Downloading", "Downloading", "正在下载", "ダウンロード中"),
        ("PendingPrefix", "Queued", "待发送", "送信待ち"),
        ("PendingFiles", "Queued: {0}", "待发送：{0}", "送信待ち：{0}"),
        ("ListSeparator", ", ", "、", "、"),
        ("FileLimitHint", "Add file · max {0}, up to {1} per message", "添加文件 · 最大 {0}，每条最多 {1} 个", "ファイルを追加 · 最大 {0}、1 件あたり {1} 個まで"),
        ("FileTooLarge", "{0} exceeds the {1} limit", "{0} 超过 {1} 上限", "{0} は上限 {1} を超えています"),
        ("TooManyFiles", "Up to {0} files per message", "每条最多 {0} 个文件", "1 件あたり最大 {0} 個のファイル"),
        ("FileSaved", "Saved {0}", "已保存 {0}", "{0} を保存しました"),
        ("Profile", "Profile", "资料", "プロフィール"),
        ("ProfileImage", "Profile image", "头像", "プロフィール画像"),
        ("ChangeAvatar", "Change avatar", "更换头像", "アバターを変更"),
        ("RemoveAvatar", "Remove avatar", "移除头像", "アバターを削除"),
        ("SaveProfile", "Save", "保存", "保存"),
        ("ProfileSaved", "Profile saved", "资料已保存", "プロフィールを保存しました"),
        ("PickAvatar", "Choose avatar", "选择头像", "アバターを選択"),
        ("InviteCopied", "Invite code copied", "已复制邀请码", "招待コードをコピーしました"),
        ("InvalidAvatar", "Avatar must be a JPEG, PNG, GIF, or WebP image.", "头像需为 JPEG、PNG、GIF 或 WebP 图片。", "アバターは JPEG、PNG、GIF、または WebP 画像にしてください。"),
        ("BlockedWord", "Message contains a blocked word.", "消息包含屏蔽词。", "メッセージに禁止ワードが含まれています。"),
        ("CooldownWait", "Wait {0} seconds before sending another message.", "请等待 {0} 秒后再发送消息。", "次のメッセージを送信するまで {0} 秒お待ちください。"),
        ("InvalidCooldown", "Message cooldown must be an integer from 0 to 600 seconds.", "发言冷却时间须为 0 到 600 秒的整数。", "送信間隔は 0～600 秒の整数で指定してください。"),
        ("ModerationSaved", "Moderation settings saved.", "管理设置已保存。", "管理設定を保存しました。"),
        ("ChatBehavior", "Chat", "聊天", "チャット"),
        ("GeneralSettings", "General", "通用", "一般"),
        ("SendShortcut", "Send shortcut", "发送快捷键", "送信ショートカット"),
        ("MessageDensity", "Message density", "消息间距", "メッセージの間隔"),
        ("ComfortableLayout", "Comfortable", "宽松布局", "ゆったり表示"),
        ("Appearance", "Appearance", "外观", "外観"),
        ("EnterToSend", "Enter to send", "Enter 发送", "Enter で送信"),
        ("CtrlEnterToSend", "Ctrl / ⌘ Enter to send", "Ctrl / ⌘ + Enter 发送", "Ctrl / ⌘ + Enter で送信"),
        ("CompactLayout", "Compact layout", "紧凑布局", "コンパクト表示"),
        ("ReduceMotion", "Reduce motion", "减少动效", "動きを減らす"),
        ("ColorScheme", "Color scheme", "颜色模式", "カラーモード"),
        ("ColorSchemeDark", "Dark", "深色模式", "ダーク"),
        ("ColorSchemeLight", "Light", "浅色模式", "ライト"),
        ("CustomBackground", "Custom background", "自定义背景", "カスタム背景"),
        ("ChooseBackground", "Choose file", "选择文件", "ファイルを選択"),
        ("RemoveBackground", "Remove", "移除", "削除"),
        ("BackgroundBlur", "Blur", "模糊", "ぼかし"),
        ("BackgroundBrightness", "Brightness", "亮度", "明るさ"),
        ("PickBackground", "Choose background", "选择背景", "背景を選択"),
        ("InvalidWallpaper", "Background must be a JPEG, PNG, GIF, or WebP image, or an MP4, WebM, MOV, or MKV video.", "背景需为 JPEG、PNG、GIF、WebP 图片，或 MP4、WebM、MOV、MKV 视频。", "背景は JPEG、PNG、GIF、WebP 画像、または MP4、WebM、MOV、MKV 動画にしてください。"),
        ("WallpaperTooLarge", "Background file exceeds the size limit.", "背景文件超过大小上限。", "背景ファイルが上限を超えています。"),
        ("WallpaperVideoUnavailable", "Video background needs a system video decoder or ffmpeg.", "视频背景需要系统解码器或 ffmpeg。", "動画背景にはシステムのデコーダーまたは ffmpeg が必要です。"),
        ("Moderation", "Moderation", "管理", "管理"),
        ("BlockedWordsHint", "Blocked words, one per line", "屏蔽词，每行一个", "禁止ワード（1行1語）"),
        ("CooldownSeconds", "Cooldown (seconds)", "发言冷却（秒）", "送信間隔（秒）"),
        ("SaveModeration", "Save moderation", "保存管理设置", "管理設定を保存"),
        ("KeyboardShortcuts", "Keyboard", "快捷键", "キーボード"),
        ("ShortcutNavigation", "Navigation", "导航", "ナビゲーション"),
        ("ShortcutPreviousChannel", "Previous channel", "上一个频道", "前のチャンネル"),
        ("ShortcutNextChannel", "Next channel", "下一个频道", "次のチャンネル"),
        ("ShortcutPreviousTab", "Previous tab", "上一个标签", "前のタブ"),
        ("ShortcutNextTab", "Next tab", "下一个标签", "次のタブ"),
        ("ShortcutPressKey", "Press keys", "按下按键", "キーを入力"),
        ("ShortcutUnbound", "None", "未绑定", "なし"),
        ("ShortcutResetAll", "Reset all", "全部恢复", "すべて戻す"),
        ("ShortcutReset", "Reset", "恢复", "戻す"),
        ("You", "You", "你", "あなた"),
        ("UserId", "User ID", "用户 ID", "ユーザーID"),
        ("CopyUserId", "Copy user ID", "复制用户 ID", "ユーザーIDをコピー"),
        ("CopyUsername", "Copy username", "复制用户名", "ユーザー名をコピー"),
        ("MentionMember", "Mention", "提及", "メンション"),
        ("KickMember", "Kick", "踢出", "キック"),
        ("BanMember", "Ban", "封禁", "BAN"),
        ("Copied", "Copied", "已复制", "コピーしました"),
        ("MarkAsRead", "Mark as read", "标为已读", "既読にする"),
        ("MarkAsUnread", "Mark as unread", "标为未读", "未読にする"),
        ("NotificationsAll", "All messages", "全部消息", "すべてのメッセージ"),
        ("NotificationsMentions", "Mentions only", "仅 @", "メンションのみ"),
        ("NotificationsMute", "Mute", "静音", "ミュート"),
        ("NewMessages", "{0} new", "{0} 条新消息", "新着 {0}"),
        ("JumpToPresent", "Jump to present", "跳到最新", "最新へ"),
        ("NewMessagesDivider", "New messages", "新消息", "新着メッセージ"),
        ("EditingMessage", "Editing message", "正在编辑消息", "メッセージを編集中"),
        ("CancelEdit", "Cancel edit", "取消编辑", "編集をキャンセル"),
        ("EditMessage", "Edit message", "编辑消息", "メッセージを編集"),
        ("Edited", "edited", "已编辑", "編集済み"),
        ("LanguagePacks", "Language packs", "语言包", "言語パック"),
        ("ImportLanguagePack", "Import", "导入", "読み込む"),
        ("ExportLanguageTemplate", "Export template", "导出模板", "テンプレートを書き出す"),
        ("DropLanguagePack", "Drop a .json language pack here", "将 .json 语言包拖到这里", "JSON 言語パックをここにドロップ"),
        ("LanguagePackHelp", "Export the template, translate it, then import to add a language or override built-in copy.", "导出模板翻译后导入，可新增语言或覆盖内置文案。", "テンプレートを書き出して翻訳し、読み込むと言語の追加や上書きができます。"),
        ("LanguagePackImported", "Imported {0} ({1} strings).", "已导入 {0}（{1} 条）。", "{0} を読み込みました（{1} 件）。"),
        ("LanguagePackInvalid", "This file is not a valid language pack.", "这不是有效的语言包。", "有効な言語パックではありません。"),
        ("LanguagePackTooLarge", "Language pack exceeds 256 KiB.", "语言包超过 256 KiB。", "言語パックが 256 KiB を超えています。"),
        ("RemoveLanguagePack", "Remove pack", "移除语言包", "パックを削除"),
        ("CustomLanguagePack", "Custom pack applied", "已应用自定义语言包", "カスタムパックを適用中"),
        ("Recent", "Recent", "最近", "最近")
    ];

    private static readonly FrozenDictionary<string, string> English = Map(Rows, row => row.En);
    private static readonly FrozenDictionary<string, string> Chinese = Map(Rows, row => row.Zh);
    private static readonly FrozenDictionary<string, string> Japanese = Map(Rows, row => row.Ja);
}

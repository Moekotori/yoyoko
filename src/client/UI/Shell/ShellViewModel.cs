using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using Chat.Core.Instances;
using Chat.Localization;
using Chat.Core.Messaging;
using Chat.Core.Realtime;
using Chat.Core.Sessions;
using Chat.Core.Voice;
using Chat.Protocol;
using Chat.UI.Appearance;
using Chat.UI.Auth;
using Chat.UI.Channels;
using Chat.UI.Chat;
using Chat.UI.Components;
using Chat.UI.Instances;
using Chat.UI.Localization;
using Chat.UI.Settings;
using Chat.UI.Shortcuts;
using Chat.UI.Voice;
using Chat.UI.Workspace;
using ChannelItem = Chat.UI.Channels.ChannelItem;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel : ObservableObject, IDisposable, IVoiceQualityHost
{
    private readonly InstanceManager _instances;
    private readonly IMessageCache _cache;
    private readonly ICredentialVault _vault;
    private readonly IInstanceDiscovery _discovery;
    private readonly IChatApiFactory _apis;
    private readonly Func<IGatewayConnection> _gateways;
    private readonly IVoiceMedia _media;
    private readonly I18n _text;
    private readonly IChatChrome _chrome;
    private readonly IShortcutPreference _shortcuts;
    private readonly ISystemTransportControls _osTransport;
    private readonly SystemTransportBinder _transport;
    private readonly CancellationToken _lifetime;
    private DateTimeOffset _cooldownUntil;
    private Guid? _cooldownChannel;
    private string _moderationWords = "";
    private string _cooldownInput = "0";
    private bool _settingsOpen;
    private readonly AttachmentPreviews _previews;
    private string _status = "";
    private string _draft = "";
    private string _communityName = "";
    private string _inviteInput = "";
    private InstanceItem? _selected;
    private ChannelItem? _selectedChannel;
    private ChannelTimeline? _timeline;
    private Action? _timelineChanged;
    private AudioQualityChoice? _selectedAudioQuality;
    private AudioQualityChoice? _selectedChannelQuality;
    private bool _qualityUpdating;
    private string _qualityUi = "";
    public ShellViewModel(InstanceManager instances, IMessageCache cache, ICredentialVault vault,
        IInstanceDiscovery discovery, IChatApiFactory apis, Func<IGatewayConnection> gateways,
        IVoiceMedia media, ILocalePreference locale, IChatChrome chrome, IVoiceDevicePreference devices,
        IAppearancePreference appearance, IShortcutPreference shortcuts, ISystemTransportPreference transport,
        ISystemTransportControls osTransport, I18n text, ILanguagePacks packs, string productName, CancellationToken lifetime, WorkspaceConnection connection)
    {
        _connection = connection;
        _instances = instances;
        _cache = cache;
        _vault = vault;
        _discovery = discovery;
        _apis = apis;
        _gateways = gateways;
        _media = media;
        _text = text;
        _chrome = chrome;
        _shortcuts = shortcuts;
        ProductName = productName;
        _lifetime = lifetime;
        _osTransport = osTransport;
        _osTransport.RaiseRequested += OnOsRaise;
        _transport = new SystemTransportBinder(osTransport, transport, productName);
        _previews = new(lifetime);
        Devices = new(media, devices, text, () => SelectedInstance?.Context.Session, lifetime);
        AuthForm = new(AuthenticateAsync, error => { AuthForm!.Status = _text.Error(error); OnError(error); });
        Connection = new(ConnectWorkspaceAsync, DisconnectWorkspaceAsync, ProbeLatencyAsync, CancelWorkspaceWork, OnError, text);
        Wallpaper = new(appearance, chrome, text);
        Settings = new(locale, chrome, appearance, shortcuts, Wallpaper, () => ShowSettings = false, () => SelectedInstance?.Context.Session, PickAvatarAsync, OnError, text, Devices, Connection, AuthForm, packs, transport, this);
        chrome.Changed += OnChromeChanged;
        shortcuts.Changed += OnShortcutsChanged;
        OpenAddInstance = new(_ => OpenAddInstanceDialog());
        CloseAddInstance = new(_ => CloseAddInstanceDialog());
        OpenSettings = new(_ =>
        {
            CloseJump();
            if (ShowSettings)
            {
                ShowSettings = false;
                return;
            }
            ShowSettings = true;
            Settings.Profile.Reload();
            _ = Devices.RefreshAsync();
        });
        OpenVoiceSettings = new(_ =>
        {
            CloseJump();
            RebuildQualityChoices();
            Settings.Section = SettingsSection.Voice;
            ShowSettings = true;
            _ = Devices.RefreshAsync();
        });
        _text.PropertyChanged += OnTextChanged;
        AddInstance = new(AddAsync, OnError);

        Send = new(SendAsync, OnError);
        AttachFile = new(AttachAsync, OnError);
        CreateServer = new(CreateServerAsync, OnError);
        CreateChannel = new(CreateChannelAsync, OnError);
        JoinServer = new(JoinAsync, OnError);
        SaveModeration = new(SaveModerationAsync, OnError);
        SignOut = new(SignOutAsync, OnError);
        ToggleMute = new(() => SetMuteAsync(true), OnError);
        ToggleDeaf = new(() => SetDeafAsync(true), OnError);
        LeaveVoice = new(LeaveVoiceAsync, OnError);
        JoinVoice = new(value => JoinVoiceChannelAsync(value as ChannelItem), OnError);
        BeginAddInstance = new(_ => OpenAddInstanceDialog());
        Workspace = new(false, text);
        InitializePresentation();
        RebuildQualityChoices();
        OpenProfile = new(_ => { if (!IsSignedIn) return; CloseJump(); Settings.Profile.Reload(); ProfileOpen = !ProfileOpen; });
        CloseProfile = new(_ => ProfileOpen = false);
    }
    public string ProductName { get; }
    public WorkspaceViewModel Workspace { get; }
    public bool IsPreview => false;
    public ActionCommand BeginAddInstance { get; }
    public ObservableCollection<InstanceItem> Instances { get; } = [];
    public ObservableCollection<ChannelItem> Channels { get; } = [];
    public ObservableCollection<MessageRow> Messages { get; } = [];
    public AsyncCommand AddInstance { get; }
    public AuthFormViewModel AuthForm { get; }
    public AsyncCommand Send { get; }
    public AsyncCommand AttachFile { get; }
    public AsyncCommand CreateServer { get; }
    public AsyncCommand JoinServer { get; }
    public AsyncCommand SaveModeration { get; }
    public AsyncCommand SignOut { get; }
    public AsyncCommand ToggleMute { get; }
    public AsyncCommand ToggleDeaf { get; }
    public AsyncCommand LeaveVoice { get; }
    public AsyncCommand JoinVoice { get; }
    public ObservableCollection<AudioQualityChoice> AudioQualityChoices { get; } = [];
    public ObservableCollection<AudioQualityChoice> ChannelQualityChoices { get; } = [];
    public AudioQualityChoice? SelectedAudioQuality
    {
        get => _selectedAudioQuality;
        set
        {
            if (_selectedAudioQuality?.Id == value?.Id) return;
            _selectedAudioQuality = value;
            Changed();
            if (!_qualityUpdating && value is not null && InVoice)
                _ = SetVoiceQualityAsync(value.Id);
        }
    }
    public AudioQualityChoice? SelectedChannelQuality
    {
        get => _selectedChannelQuality;
        set
        {
            if (_selectedChannelQuality?.Id == value?.Id) return;
            _selectedChannelQuality = value;
            Changed();
            if (!_qualityUpdating && value is not null && CanSetChannelQuality)
                _ = SetChannelQualityAsync(value.Id);
        }
    }
    public bool CanSetChannelQuality
    {
        get
        {
            var session = SelectedInstance?.Context.Session;
            var channel = SelectedChannel;
            if (session is null || channel is null || channel.Kind != "voice") return false;
            return session.Servers.Any(server => server.Id == channel.ServerId && server.OwnerId == session.Account.Key.Id);
        }
    }
    public ActionCommand OpenAddInstance { get; }
    public ActionCommand CloseAddInstance { get; }
    public ActionCommand OpenSettings { get; }
    public ActionCommand OpenVoiceSettings { get; }
    public SettingsViewModel Settings { get; }
    public WallpaperSession Wallpaper { get; }
    public VoiceDevicesViewModel Devices { get; }
    public Func<Task<IReadOnlyList<PickedFile>>>? PickFiles { get; set; }
    public Func<Task<PickedFile?>>? PickAvatar { get; set; }
    public Func<string, Task<Stream?>>? OpenSaveStream { get; set; }
    public Action? ScrollToLatest { get; set; }
    public Action? ScrollToUnread { get; set; }
    public Func<bool>? IsNearBottom { get; set; }
    public Action? FocusComposer { get; set; }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string Username { get => AuthForm.Username; set => AuthForm.Username = value; }
    public string DisplayName { get => AuthForm.DisplayName; set => AuthForm.DisplayName = value; }
    public string Password { get => AuthForm.Password; set => AuthForm.Password = value; }
    public string Draft { get => _draft; set { _draft = value; Changed(); Changed(nameof(CanSend)); } }
    public string CommunityName { get => _communityName; set { _communityName = value; Changed(); } }
    public string InviteInput { get => _inviteInput; set { _inviteInput = value; Changed(); } }
    public bool EnterToSend => _chrome.EnterToSend;
    public bool CompactLayout => _chrome.Compact;
    public bool ReduceMotion => _chrome.ReduceMotion;
    public string ModerationWords { get => _moderationWords; set { _moderationWords = value; Changed(); } }
    public string CooldownInput { get => _cooldownInput; set { _cooldownInput = value; Changed(); } }
    public int CooldownLeft
    {
        get
        {
            if (SelectedChannel?.Id != _cooldownChannel) return 0;
            var left = (int)Math.Ceiling((_cooldownUntil - DateTimeOffset.UtcNow).TotalSeconds);
            return Math.Max(0, left);
        }
    }
    public bool OnCooldown => CooldownLeft > 0;
    public bool ChannelForbidden => _timeline?.AccessDenied == true;
    public bool CanSend => !ChannelForbidden && !OnCooldown && (_timeline?.CanQueue ?? true)
        && (IsEditing ? Draft.Trim().Length > 0 : Draft.Trim().Length > 0 || HasPending);
    public bool HasOlder => _timeline?.HasOlder == true;
    public ServerDto? ActiveServer
    {
        get
        {
            var session = SelectedInstance?.Context.Session;
            if (session is null) return null;
            if (SelectedChannel is { } channel)
                return session.Servers.FirstOrDefault(item => item.Id == channel.ServerId)
                    ?? session.Servers.FirstOrDefault();
            return session.Servers.FirstOrDefault();
        }
    }
    public bool CanModerate
    {
        get
        {
            var account = SelectedInstance?.Context.Account;
            return account is not null && ActiveServer?.OwnerId == account.Key.Id;
        }
    }
    public InstanceItem? SelectedInstance
    {
        get => _selected;
        set
        {
            _selected = value;
            _settingsOpen = false;
            ClearPlaybacks();
            Changed();
            Changed(nameof(InstanceName));
            Changed(nameof(InstanceHost));
            Changed(nameof(ShowAddInstance));
            Changed(nameof(ShowAuth));
            Changed(nameof(ShowChannelNav));
            Changed(nameof(ShowChat));
            Changed(nameof(ShowSettings));
            Changed(nameof(IsSignedIn));
            Changed(nameof(PaneTitle));
            Changed(nameof(AccountName));
            Changed(nameof(AccountHandle));
            Changed(nameof(SelfUsername));
            Changed(nameof(SelfDisplayName));
            Changed(nameof(AccountInitial));
            Changed(nameof(AccountPlayback));
            Changed(nameof(HasAvatar));
            Changed(nameof(InviteHint));
            Changed(nameof(InviteCode));
            Changed(nameof(HasInvite));
            _ = LoadSessionVisualsAsync();
            RefreshCommunity();
        }
    }
    public ChannelItem? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (ReferenceEquals(_selectedChannel, value)) return;
            _selectedChannel = value;
            Changed();
            Changed(nameof(PaneTitle));
            Changed(nameof(ShowChat));
            Changed(nameof(ActiveServer));
            Changed(nameof(CanModerate));
            FillModeration();
            NotifyCooldown();
            _ = OpenChannelAsync();
        }
    }
    public string InstanceName => SelectedInstance?.Name ?? _text.Get(TextKey.Workspace);
    public string InstanceHost => SelectedInstance?.Host ?? _text.Get(TextKey.NotConnected);
    public bool IsSignedIn => SelectedInstance?.Context.Session is not null;
    public bool ShowSettings
    {
        get => _settingsOpen;
        private set
        {
            if (_settingsOpen == value) return;
            _settingsOpen = value;
            Changed();
            Changed(nameof(SettingsShortcutTip));
            Changed(nameof(ShowAddInstance));
            Changed(nameof(ShowAuth));
            Changed(nameof(ShowChat));
            Changed(nameof(PaneTitle));
            Connection.SetWatching(value && Settings.Section == SettingsSection.Connection);
        }
    }
    public bool ShowAuth => !IsSignedIn && !ShowSettings && SelectedChannel is null;
    public bool ShowGuest => !IsSignedIn;
    public bool ShowChannelNav => IsSignedIn || Channels.Count > 0;
    public bool ShowChat => !ShowSettings && SelectedChannel is { CanChat: true };
    public bool InVoice => SelectedInstance?.Context.Session?.Voice.Joined == true;
    public bool InSelectedVoice =>
        SelectedChannel is { Kind: "voice" } channel && IsJoinedVoice(channel);
    public string ConnectedVoiceName =>
        SelectedInstance?.Context.Session?.Voice.ChannelId is Guid id
            ? Channels.FirstOrDefault(item => item.Id == id)?.Name ?? _text.Get(TextKey.Voice)
            : "";
    public string VoiceSessionLabel
    {
        get
        {
            var voice = SelectedInstance?.Context.Session?.Voice;
            if (voice is null || !voice.Joined) return "";
            if (voice.Reconnecting) return _text.Get(TextKey.VoiceReconnecting);
            return string.IsNullOrEmpty(voice.MediaError)
                ? _text.Get(TextKey.VoiceConnected)
                : _text.Get(TextKey.VoiceAudioOff);
        }
    }
    public bool IsJoinedVoice(ChannelItem channel) =>
        SelectedInstance?.Context.Session?.Voice.ChannelId == channel.Id;
    public string MuteLabel => SelectedInstance?.Context.Session?.Voice.SelfMute == true ? _text.Get(TextKey.Unmute) : _text.Get(TextKey.Mute);
    public string DeafLabel => SelectedInstance?.Context.Session?.Voice.SelfDeaf == true ? _text.Get(TextKey.Undeafen) : _text.Get(TextKey.Deafen);
    public string VoiceStatus
    {
        get
        {
            var voice = SelectedInstance?.Context.Session?.Voice;
            if (voice is null) return "";
            if (voice.Reconnecting) return _text.Get(TextKey.VoiceReconnecting);
            if (voice.MediaError is { Length: > 0 } error) return _text.Get(TextKey.VoiceMediaError, error);
            return voice.Joined ? _text.Get(TextKey.VoiceConnected) : _text.Get(TextKey.VoiceJoinHint);
        }
    }
    public IEnumerable<string> VoiceMembers =>
        SelectedChannel is null ? [] :
        SelectedInstance?.Context.Session?.Voice.InChannel(SelectedChannel.Id)
            .Select(state => state.DisplayName
                + (string.IsNullOrEmpty(state.AudioQuality) ? "" : " · " + QualityChoice(state.AudioQuality).Title)
                + (state.SelfMute ? _text.Get(TextKey.MutedSuffix) : "")
                + (state.SelfDeaf ? _text.Get(TextKey.DeafenedSuffix) : ""))
        ?? [];
    public string PaneTitle => ShowSettings ? _text.Get(TextKey.Settings)
        : SelectedChannel?.Label ?? (ShowAuth ? _text.Get(TextKey.ConnectServer) : ProductName);
    public string AccountName => SelectedInstance?.Context.Account?.DisplayName ?? "";
    public string AccountInitial => Avatar.FromName(AccountName);
    public string AccountHandle => SelectedInstance?.Context.Account is { Username: { Length: > 0 } name } ? "@" + name : "";
    public string SelfUsername => SelectedInstance?.Context.Session?.Me.Username ?? "";
    public string SelfDisplayName => SelectedInstance?.Context.Session?.Me.DisplayName ?? "";
    public AvatarPlayback? AccountPlayback { get; private set; }
    public bool HasAvatar => AccountPlayback is not null;
    public string InviteCode => SelectedInstance?.Context.Session?.Servers.FirstOrDefault()?.InviteCode ?? "";
    public bool HasInvite => InviteCode.Length > 0;
    public string InviteHint
    {
        get
        {
            return HasInvite ? _text.Get(TextKey.InviteHint, InviteCode) : _text.Get(TextKey.InviteHintEmpty);
        }
    }
    public ObservableCollection<PendingFileItem> PendingFiles { get; } = [];
    public bool HasPending => PendingFiles.Count > 0;
    public int AttachmentLimit => Math.Clamp(
        SelectedInstance?.Context.Session?.MaxAttachments ?? ProtocolVersion.MaxAttachmentsPerMessage,
        1, IncomingFiles.MaxUiAttachments);
    public string FileLimitTip
    {
        get
        {
            var session = SelectedInstance?.Context.Session;
            var max = FileKinds.SizeLabel(session?.MaxAttachmentBytes ?? ProtocolVersion.MaxAttachmentBytes);
            return _text.Get(TextKey.FileLimitHint, max, AttachmentLimit);
        }
    }

    public async Task InitializeAsync(Func<CancellationToken, Task> initializeCache,
        Func<CancellationToken, Task<InstanceContext?>>? prepareWorkspace = null)
    {
        try
        {
            await initializeCache(_lifetime);
            await _instances.LoadCachedAsync(_lifetime);
            Connection.Address = await _connection.StartupAddressAsync(_lifetime);
            var work = StartWorkspaceWork();
            Connection.IsBusy = true;
            Connection.Status = _text.Get(TextKey.ConnectingServer);
            InstanceContext? preferred = null;
            Exception? preparationError = null;
            if (prepareWorkspace is not null)
            {
                try { preferred = await prepareWorkspace(work); }
                catch (OperationCanceledException) when (work.IsCancellationRequested)
                {
                    preparationError = null;
                    Connection.Status = _text.Get(TextKey.Cancelled);
                }
                catch (Exception exception) { preparationError = exception; }
            }
            foreach (var item in _instances.Contexts) Instances.Add(new(item));
            SelectedInstance = Instances.FirstOrDefault(item => item.Context == preferred)
                ?? Instances.FirstOrDefault(item => item.Context.Descriptor.BaseUrl.AbsoluteUri.TrimEnd('/') == Connection.Address.TrimEnd('/'))
                ?? Instances.FirstOrDefault();
            foreach (var item in Instances.ToList())
            {
                if (work.IsCancellationRequested) break;
                if (item.Context.Session is { } session)
                {
                    BindSession(session);
                    if (SelectedInstance == item) RefreshCommunity();
                }
                else await RestoreAsync(item, work);
            }
            if (preparationError is not null) OnError(preparationError);
            if (work.IsCancellationRequested) Connection.Status = _text.Get(TextKey.Cancelled);
            else Connection.Status = preparationError is null ? "" : _text.Error(preparationError);
            Connection.Bind(SelectedInstance?.Context);
            Settings.Profile.Reload();
        }
        catch (Exception exception) { Connection.Status = Status = _text.Get(TextKey.CacheLoadFailed, exception.Message); }
        finally { FinishWorkspaceWork(); Connection.IsBusy = false; Connection.Bind(SelectedInstance?.Context); }
    }

    private async Task RestoreAsync(InstanceItem item, CancellationToken token)
    {
        try
        {
            var info = await _discovery.DiscoverAsync(item.Context.Descriptor.BaseUrl, token);
            var session = await InstanceSession.RestoreAsync(item.Context.Descriptor, _apis.Create(info.Api, info.Gateway),
                _cache, _vault, _gateways, _media, token);
            if (session is null) return;
            session.ApplyDiscovery(info);
            item.Context.AttachDiscovery(info);
            item.Context.AttachSession(session);
            BindSession(session);
            if (SelectedInstance == item) RefreshCommunity();
        }
        catch { /* offline: stay on login */ }
    }

    private async Task AuthenticateAsync(bool register)
    {
        var item = SelectedInstance ?? throw new InvalidOperationException(_text.Get(TextKey.NeedInstance));
        Status = register ? _text.Get(TextKey.Registering) : _text.Get(TextKey.SigningIn);
        var info = await _discovery.DiscoverAsync(item.Context.Descriptor.BaseUrl, _lifetime);
        var session = await InstanceSession.SignInAsync(item.Context.Descriptor, _apis.Create(info.Api, info.Gateway),
            _cache, _vault, _gateways, _media, Username.Trim(), Password, string.IsNullOrWhiteSpace(DisplayName) ? Username.Trim() : DisplayName.Trim(),
            register, _lifetime);
        session.ApplyDiscovery(info);
        item.Context.AttachDiscovery(info);
        if (item.Context.Session is { } previous)
        {
            UnbindSession(previous);
            await previous.DisposeAsync();
        }
        item.Context.AttachSessionClear();
        item.Context.AttachSession(session);
        BindSession(session);
        Password = "";
        AuthForm.Status = "";
        Status = "";
        Settings.Profile.Reload();
        Connection.Status = "";
        Connection.Bind(item.Context);
        RefreshCommunity();
    }

    public event Action? ActivateRequested;
    public void BindOsTransport(nint hwnd) => _transport.BindWindow(hwnd);
    private void OnOsRaise() => ActivateRequested?.Invoke();

    private void BindSession(InstanceSession session)
    {
        if (!_boundSessions.Add(session)) return;
        _transport.Attach(session);
        session.Voice.ApplyRoute(Devices.Route);
        session.CommunityChanged += () => Dispatcher.UIThread.Post(RefreshCommunity);
        session.Voice.Changed += () => Dispatcher.UIThread.Post(NotifyVoice);
        session.MessageArrived += message => Dispatcher.UIThread.Post(() =>
        {
            _ = NoteArrivalAsync(session, message);
            ForgetTyping(message.ChannelId, message.AuthorId);
            if (SelectedChannel?.Id == message.ChannelId)
            {
                _timeline?.ApplyRemote(message);
                if (!CanObserveTimeline || _awayFromBottom || IsNearBottom?.Invoke() != true)
                {
                    _newWhileAway++;
                    _awayFromBottom = true;
                    Changed(nameof(ShowJumpBar));
                    Changed(nameof(JumpBarLabel));
                }
                else if (LatestVisibleId() is Guid latest)
                    _ = MarkReadAsync(message.ChannelId, latest);
                SyncMessages();
            }
            else ApplyInbox();
        });
        session.MessageUpdated += message => Dispatcher.UIThread.Post(() =>
        {
            _timeline?.ApplyRemote(message);
            SyncMessages();
        });
        session.MessageDeleted += deleted => Dispatcher.UIThread.Post(() =>
        {
            if (IsEditing && _editingId == deleted.Id) CancelEdit();
            if (IsReplying && _replyTo == deleted.Id) CancelReply();
            if (SelectedChannel?.Id == deleted.ChannelId)
            {
                _timeline?.RemoveRemote(deleted.Id);
                SyncMessages();
            }
        });
        session.TypingStarted += typing => Dispatcher.UIThread.Post(() => NoteTyping(session, typing));
        session.ResyncNeeded += () => Dispatcher.UIThread.Post(() =>
        {
            if (_timeline is null || _channelLoad is not { } cancellation) return;
            _ = _timeline.LoadLatestAsync(cancellation.Token);
        });
        session.Outbound.Changed += _ => Dispatcher.UIThread.Post(() => Changed(nameof(CanSend)));
        _ = ReloadInboxAsync(session);
    }

    private void RefreshCommunity()
    {
        var session = SelectedInstance?.Context.Session;
        Channels.Clear();
        if (session is null)
        {
            SelectedChannel = null;
            NotifySession();
            Connection.Bind(SelectedInstance?.Context);
            return;
        }
        foreach (var channel in session.Channels)
        {
            var item = new ChannelItem(channel.Id, channel.ServerId ?? Guid.Empty, session.ChannelTitle(channel),
                channel.Kind, channel.AudioQuality, false, channel.Participants);
            if (_enteringChannelId == channel.Id) item.RequestEnter();
            Channels.Add(item);
        }
        _enteringChannelId = null;
        if (SelectedChannel is null || Channels.All(item => item.Id != SelectedChannel.Id))
            SelectedChannel = Channels.FirstOrDefault(channel => channel.Kind == "text") ?? Channels.FirstOrDefault();
        if (SelectedChannel is { } selected && Channels.FirstOrDefault(item => item.Id == selected.Id) is { } updated)
        {
            selected.Name = updated.Name;
            Changed(nameof(ComposerPlaceholder));
        }
        FillModeration();
        NotifySession();
        Connection.Bind(SelectedInstance?.Context);
        if (session is not null) _ = ReloadInboxAsync(session);
        _ = SyncVoiceCapAsync();
    }

    private void FillModeration()
    {
        var server = ActiveServer;
        ModerationWords = server?.BlockedWords is { Length: > 0 } words
            ? string.Join(Environment.NewLine, words)
            : "";
        CooldownInput = (server?.CooldownSeconds ?? 0).ToString();
    }

    private void NotifySession()
    {
        foreach (var instance in Instances) instance.RefreshIdentity();
        Changed(nameof(IsSignedIn));
        Changed(nameof(ShowAuth));
        Changed(nameof(ShowGuest));
        Changed(nameof(ShowChannelNav));
        Changed(nameof(ShowChat));
        Changed(nameof(AccountName));
        Changed(nameof(AccountHandle));
        Changed(nameof(SelfUsername));
        Changed(nameof(SelfDisplayName));
        Changed(nameof(AccountInitial));
        Changed(nameof(AccountPlayback));
        Changed(nameof(HasAvatar));
        Changed(nameof(InviteHint));
        Changed(nameof(InviteCode));
        Changed(nameof(HasInvite));
        Changed(nameof(PaneTitle));
        Changed(nameof(ShowSettings));
        if (SelectedInstance?.Context.Session is { } session)
        {
            _ = LoadUserAvatarAsync(session.Me.Id);
            _ = LoadUserBannerAsync(session.Me.Id);
            foreach (var id in _banners.Keys.Where(id => id != session.Me.Id).ToArray())
                _ = LoadUserBannerAsync(id);
        }
        else
            ClearPlaybacks();
        NotifyVoice();
        Changed(nameof(CanModerate));
        Changed(nameof(ActiveServer));
        Changed(nameof(CanSend));
        RefreshParticipants();
    }

    private void OnTextChanged(object? sender, PropertyChangedEventArgs args) => OnLocaleChanged();
    private void OnLocaleChanged()
    {
        foreach (var channel in Channels) channel.Refresh();
        Changed(nameof(InstanceName));
        Changed(nameof(InstanceHost));
        Changed(nameof(PaneTitle));
        Changed(nameof(InviteHint));
        Changed(nameof(InviteCode));
        Changed(nameof(HasInvite));
        Changed(nameof(MuteLabel));
        Changed(nameof(DeafLabel));
        Changed(nameof(VoiceStatus));
        Changed(nameof(VoiceMembers));
        Changed(nameof(VoiceSessionLabel));
        Changed(nameof(ConnectedVoiceName));
        Changed(nameof(InSelectedVoice));
        Changed(nameof(FileLimitTip));
        OnShortcutsChanged();
        Changed(nameof(ComposerPlaceholder));
        Changed(nameof(SendTip));
        Changed(nameof(ComposerBannerText));
        Changed(nameof(ComposerCancelTip));
        Changed(nameof(EmptyMessageTitle));
        RefreshTyping();
        RefreshParticipants();
        RebuildQualityChoices();
        Devices.Relabel();
        RefreshTyping();
    }

    private void NotifyVoice()
    {
        Changed(nameof(ShowChat));
        Changed(nameof(InVoice));
        Changed(nameof(IsSelfMuted));
        Changed(nameof(IsSelfDeafened));
        Changed(nameof(MuteLabel));
        Changed(nameof(DeafLabel));
        Changed(nameof(MuteTip));
        Changed(nameof(DeafTip));
        Changed(nameof(VoiceStatus));
        Changed(nameof(VoiceMembers));
        Changed(nameof(InSelectedVoice));
        Changed(nameof(ConnectedVoiceName));
        Changed(nameof(VoiceSessionLabel));
        SyncVoiceRoster();
        var voice = SelectedInstance?.Context.Session?.Voice;
        var key = (voice?.MaxQuality ?? "") + "/" + (voice?.Quality ?? "");
        if (key != _qualityUi)
        {
            _qualityUi = key;
            RebuildQualityChoices();
        }
        else Changed(nameof(CanSetChannelQuality));
        _ = SyncEncoderAsync();
    }

    private void SyncVoiceRoster()
    {
        var voice = SelectedInstance?.Context.Session?.Voice;
        var joined = voice?.ChannelId;
        foreach (var channel in Channels)
        {
            if (!channel.IsVoice)
            {
                if (channel.IsConnected) channel.IsConnected = false;
                if (channel.HasVoiceMembers) channel.SyncMembers([]);
                continue;
            }
            channel.IsConnected = joined == channel.Id;
            var session = SelectedInstance?.Context.Session;
            channel.SyncMembers(voice is null
                ? []
                : voice.InChannel(channel.Id)
                    .Select(state =>
                    {
                        var user = session?.User(state.UserId);
                        var playback = _playbacks.TryGetValue(state.UserId, out var cached) ? cached.Playback : null;
                        _ = LoadUserAvatarAsync(state.UserId);
                        return new VoiceMemberRow(state.UserId, state.DisplayName, user?.Username ?? "",
                            state.SelfMute, state.SelfDeaf, session?.Me.Id == state.UserId,
                            ProfileOf(state.UserId, state.DisplayName), playback, voice.IsSpeaking(state.UserId));
                    })
                    .ToList());
        }
    }

    private void RebuildQualityChoices()
    {
        _qualityUpdating = true;
        AudioQualityChoices.Clear();
        ChannelQualityChoices.Clear();
        var max = SelectedInstance?.Context.Session?.Voice.MaxQuality
            ?? SelectedChannel?.AudioQuality
            ?? AudioQualities.Studio;
        foreach (var id in AudioQualities.All)
        {
            var choice = QualityChoice(id);
            ChannelQualityChoices.Add(choice);
            if (AudioQualities.Rank(id) <= AudioQualities.Rank(max))
                AudioQualityChoices.Add(choice);
        }
        var current = SelectedInstance?.Context.Session?.Voice.Quality
            ?? SelectedInstance?.Context.Session?.Voice.Preferred
            ?? AudioQualities.Studio;
        _selectedAudioQuality = AudioQualityChoices.FirstOrDefault(item => item.Id == current)
            ?? AudioQualityChoices.LastOrDefault();
        _selectedChannelQuality = ChannelQualityChoices.FirstOrDefault(item => item.Id == max)
            ?? ChannelQualityChoices.LastOrDefault();
        _qualityUpdating = false;
        Changed(nameof(SelectedAudioQuality));
        Changed(nameof(SelectedChannelQuality));
        Changed(nameof(CanSetChannelQuality));
    }

    private AudioQualityChoice QualityChoice(string id) => id switch
    {
        AudioQualities.Standard => new(id, _text.Get(TextKey.QualityStandard), _text.Get(TextKey.QualityStandardDetail)),
        AudioQualities.High => new(id, _text.Get(TextKey.QualityHigh), _text.Get(TextKey.QualityHighDetail)),
        AudioQualities.VeryHigh => new(id, _text.Get(TextKey.QualityVeryHigh), _text.Get(TextKey.QualityVeryHighDetail)),
        _ => new(id, _text.Get(TextKey.QualityStudio), _text.Get(TextKey.QualityStudioDetail))
    };

    private Task LoadSessionVisualsAsync()
    {
        RefreshCommunity();
        return Task.CompletedTask;
    }

    private void SyncMessages()
    {
        if (_visualBudget.Suspended || IsUltraLightParked) return;
        if (_timeline is null)
        {
            Messages.Clear();
            _previews.Clear();
            RefreshMessagePresentation();
            Changed(nameof(HasOlder));
            return;
        }
        var session = SelectedInstance?.Context.Session;
        var previousFirst = Messages.Count == 0 ? Guid.Empty : Messages[0].Item.LocalId;
        var previousLast = Messages.Count == 0 ? Guid.Empty : Messages[^1].Item.LocalId;
        var ordered = new List<MessageRow>(_timeline.Items.Count);
        foreach (var item in _timeline.Items)
        {
            var row = Messages.FirstOrDefault(existing => existing.Item.LocalId == item.LocalId);
            if (row is null)
                row = new(item, session?.AuthorName(item.Message.AuthorId) ?? "", SaveAttachmentAsync, RetryFailedAsync,
                    CancelSendAsync, OnError, session is not null && item.Message.AuthorId == session.Me.Id);
            else
            {
                row.Update(item);
                row.SetAuthor(session?.AuthorName(item.Message.AuthorId) ?? row.Author,
                    session is not null && item.Message.AuthorId == session.Me.Id);
            }
            if (_playbacks.TryGetValue(item.Message.AuthorId, out var cached))
                row.Playback = cached.Playback;
            row.SetModerate(session is not null && ActiveServer?.OwnerId == session.Me.Id);
            row.ReplyLabel = ReplyPreview(item.Message, session);
            ordered.Add(row);
        }
        for (var i = 0; i < ordered.Count; i++)
        {
            if (i < Messages.Count && ReferenceEquals(Messages[i], ordered[i])) continue;
            var found = Messages.IndexOf(ordered[i]);
            if (found >= 0) Messages.Move(found, i);
            else Messages.Insert(i, ordered[i]);
        }
        while (Messages.Count > ordered.Count)
            Messages.RemoveAt(Messages.Count - 1);
        RefreshMessagePresentation();
        RefreshAvatars();
        if (session is not null)
            _previews.Update(Messages.SelectMany(row => row.Files), session.DownloadAsync);
        Changed(nameof(HasOlder));
        Changed(nameof(ChannelForbidden));
        Changed(nameof(CanSend));
        var prepended = previousFirst != Guid.Empty && ordered.Count > 0
            && ordered[0].Item.LocalId != previousFirst
            && ordered.Exists(row => row.Item.LocalId == previousFirst);
        var appended = ordered.Count > 0 && ordered[^1].Item.LocalId != previousLast && !prepended;
        var keepLast = previousLast != Guid.Empty && ordered.Exists(row => row.Item.LocalId == previousLast);
        if (appended && keepLast && !ordered[^1].IsOwn)
            ordered[^1].RequestEnter();
        if (appended && (ordered[^1].IsPending || (IsNearBottom?.Invoke() ?? true)))
            ScrollToLatest?.Invoke();
    }

    private Task SendAsync()
    {
        if (_timeline is null || !CanSend) return Task.CompletedTask;
        var text = Draft.Trim();
        if (IsEditing) return CommitEditAsync(text);
        if (text.Length == 0 && PendingFiles.Count == 0) return Task.CompletedTask;
        if (text.Length > 0 && ContainsBlocked(text))
        {
            Status = _text.Get(TextKey.BlockedWord);
            return Task.CompletedTask;
        }
        if (!_timeline.CanQueue) return Task.CompletedTask;
        var files = PendingFiles.Select(item => item.File).ToList();
        foreach (var file in files)
            if (file.Content.CanSeek) file.Content.Position = 0;
        var replyTo = _replyTo;
        var timeline = _timeline;
        var channel = SelectedChannel;
        Draft = "";
        Status = "";
        PendingFiles.Clear();
        NotifyPending();
        CancelReply();
        FocusComposer?.Invoke();
        _ = FinishSendAsync(timeline, text, files, replyTo, channel);
        return Task.CompletedTask;
    }

    private async Task CommitEditAsync(string text)
    {
        if (text.Length == 0 || _editingId is not Guid editing) return;
        if (ContainsBlocked(text))
        {
            Status = _text.Get(TextKey.BlockedWord);
            return;
        }
        Status = "";
        await _timeline!.EditAsync(editing, text, _lifetime);
        CancelEdit();
        FocusComposer?.Invoke();
    }

    private async Task FinishSendAsync(ChannelTimeline timeline, string text, List<PickedFile> files, Guid? replyTo,
        ChannelItem? channel)
    {
        try
        {
            await timeline.SendAsync(text.Length == 0 ? null : text, files, replyTo, _lifetime);
            if (!ReferenceEquals(_timeline, timeline)) return;
            if (channel is { } selected && SelectedChannel?.Id == selected.Id && LatestVisibleId() is Guid latest)
                _ = MarkReadAsync(selected.Id, latest);
            BeginCooldown(ActiveServer?.CooldownSeconds ?? 0);
        }
        catch (ChatApiException exception) when (exception.Code == "blocked_word")
        {
            if (ReferenceEquals(_timeline, timeline))
            {
                Draft = text;
                var failed = timeline.Items.LastOrDefault(item => item.Status == SendStatus.Failed);
                if (failed is not null) timeline.DropFailed(failed.LocalId);
            }
            OnError(exception);
        }
        catch (Exception exception)
        {
            OnError(exception);
        }
    }

    private bool ContainsBlocked(string text)
    {
        var words = ActiveServer?.BlockedWords;
        if (words is null || words.Length == 0) return false;
        foreach (var word in words)
            if (word.Length > 0 && text.Contains(word, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private async Task RetryFailedAsync(TimelineItem item)
    {
        if (_timeline is null) return;
        await _timeline.RetryAsync(item.LocalId, _lifetime);
        ScrollToLatest?.Invoke();
        BeginCooldown(ActiveServer?.CooldownSeconds ?? 0);
        FocusComposer?.Invoke();
    }

    private async Task CancelSendAsync(TimelineItem item)
    {
        if (_timeline is null) return;
        await _timeline.CancelAsync(item.LocalId, _lifetime);
        Changed(nameof(CanSend));
    }

    public async Task QueueFilesAsync(IReadOnlyList<PickedFile> files)
    {
        var session = SelectedInstance?.Context.Session;
        var max = session?.MaxAttachmentBytes ?? ProtocolVersion.MaxAttachmentBytes;
        var cap = AttachmentLimit;
        foreach (var file in files)
        {
            if (file.Size <= 0)
            {
                await file.DisposeAsync();
                continue;
            }
            if (file.Size > max)
            {
                await file.DisposeAsync();
                Status = _text.Get(TextKey.FileTooLarge, file.FileName, FileKinds.SizeLabel(max));
                continue;
            }
            if (PendingFiles.Count >= cap)
            {
                await file.DisposeAsync();
                Status = _text.Get(TextKey.TooManyFiles, cap);
                continue;
            }
            PendingFiles.Add(new(file, RemovePending));
        }
        NotifyPending();
    }

    private void RemovePending(PendingFileItem item)
    {
        if (!PendingFiles.Remove(item)) return;
        item.File.Content.Dispose();
        NotifyPending();
    }

    private async Task AttachAsync()
    {
        if (IsEditing)
        {
            Workspace.ShowNotice(_text.Get(TextKey.CannotAttachWhileEditing));
            return;
        }
        if (!ShowChat || ChannelForbidden || !IsSignedIn) return;
        if (PickFiles is null) return;
        await QueueFilesAsync(await PickFiles());
    }

    private void NotifyPending()
    {
        Changed(nameof(HasPending));
        Changed(nameof(FileLimitTip));
        Changed(nameof(CanSend));
        if (Status.StartsWith(_text.Get(TextKey.PendingPrefix), StringComparison.Ordinal))
            Status = "";
    }

    private async Task SaveAttachmentAsync(AttachmentDto dto)
    {
        try
        {
            if (OpenSaveStream is null || dto.Id == Guid.Empty) return;
            await using var output = await OpenSaveStream(dto.FileName);
            if (output is null) return;
            var session = SelectedInstance?.Context.Session ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
            await session.DownloadToAsync(dto, output, _lifetime);
            Status = _text.Get(TextKey.FileSaved, dto.FileName);
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
    }

    private async Task CreateServerAsync()
    {
        var session = SelectedInstance?.Context.Session ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        var name = string.IsNullOrWhiteSpace(CommunityName) ? "Community" : CommunityName.Trim();
        var server = await session.CreateServerAsync(name, _lifetime);
        if (SelectedInstance?.Context.Session != session) return;
        CommunityName = "";
        RefreshCommunity();
        SelectedChannel = Channels.FirstOrDefault(channel => channel.ServerId == server.Id && channel.Kind == "text");
    }

    private async Task JoinAsync()
    {
        var session = SelectedInstance?.Context.Session ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        await session.JoinAsync(InviteInput, _lifetime);
        InviteInput = "";
        RefreshCommunity();
    }

    private async Task SignOutAsync()
    {
        var item = SelectedInstance ?? throw new InvalidOperationException(_text.Get(TextKey.NoInstanceSelected));
        if (item.Context.Session is { } session)
        {
            UnbindSession(session);
            await session.SignOutAsync();
        }
        ClearAccountDrafts();
        item.Context.AttachSessionClear();
        DetachTimeline();
        foreach (var pending in PendingFiles.ToList())
            pending.File.Content.Dispose();
        PendingFiles.Clear();
        NotifyPending();
        Channels.Clear();
        Messages.Clear();
        SelectedChannel = null;
        ClearPlaybacks();
        NotifySession();
        Changed(nameof(ShowAuth));
        Changed(nameof(ShowGuest));
        Changed(nameof(IsSignedIn));
        Connection.Bind(item.Context);
    }

    private Task SetMuteAsync(bool _)
    {
        var voice = SelectedInstance?.Context.Session?.Voice ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        return voice.SetMuteAsync(!voice.SelfMute, _lifetime);
    }

    private Task SetDeafAsync(bool _)
    {
        var voice = SelectedInstance?.Context.Session?.Voice ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        return voice.SetDeafAsync(!voice.SelfDeaf, _lifetime);
    }

    private Task LeaveVoiceAsync()
    {
        var voice = SelectedInstance?.Context.Session?.Voice ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        return voice.LeaveAsync(_lifetime);
    }

    private Task SetVoiceQualityAsync(string quality)
    {
        var voice = SelectedInstance?.Context.Session?.Voice ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        return voice.SetQualityAsync(quality, _lifetime);
    }

    private async Task SetChannelQualityAsync(string quality)
    {
        var session = SelectedInstance?.Context.Session ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        var channel = SelectedChannel ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        await session.Voice.SetChannelMaxQualityAsync(channel.Id, quality, _lifetime);
        channel.AudioQuality = session.Voice.MaxQuality;
    }

    private async Task SyncVoiceCapAsync()
    {
        try
        {
            var session = SelectedInstance?.Context.Session;
            if (session?.Voice.ChannelId is not Guid id) return;
            var cap = session.Channels.FirstOrDefault(item => item.Id == id)?.AudioQuality;
            await session.Voice.ApplyChannelMaxAsync(id, cap, _lifetime);
        }
        catch (Exception exception) { Status = _text.Error(exception); }
    }

    private async Task SyncEncoderAsync()
    {
        try
        {
            var voice = SelectedInstance?.Context.Session?.Voice;
            if (voice is null) return;
            await voice.SyncEncoderAsync(_lifetime);
        }
        catch (Exception exception) { Status = _text.Error(exception); }
    }

    private async Task<PickedFile?> PickAvatarAsync() =>
        PickAvatar is null ? null : await PickAvatar();

    private void OnError(Exception exception)
    {
        Status = exception switch
        {
            ChatApiException { Code: "blocked_word" } => _text.Get(TextKey.BlockedWord),
            ChatApiException { Code: "cooldown", RetryAfterSeconds: int seconds } => _text.Get(TextKey.CooldownWait, seconds),
            ChatApiException { Code: "cooldown" } => _text.Get(TextKey.CooldownWait, Math.Max(1, CooldownLeft)),
            _ => _text.Error(exception)
        };
        if (exception is ChatApiException { RetryAfterSeconds: int wait })
            BeginCooldown(wait);
    }

    internal void ReportInteractionError(Exception exception) => OnError(exception);

    private void OnChromeChanged()
    {
        Changed(nameof(UltraLightEnabled));
        Changed(nameof(EnterToSend));
        Changed(nameof(CompactLayout));
        Changed(nameof(ReduceMotion));
        Changed(nameof(SendTip));
    }

    private void NotifyCooldown()
    {
        Changed(nameof(CooldownLeft));
        Changed(nameof(OnCooldown));
        Changed(nameof(CanSend));
        Changed(nameof(ComposerPlaceholder));
        Changed(nameof(SendTip));
    }

    private void BeginCooldown(int seconds)
    {
        if (seconds <= 0) return;
        _cooldownChannel = SelectedChannel?.Id;
        _cooldownUntil = DateTimeOffset.UtcNow.AddSeconds(seconds);
        NotifyCooldown();
        DispatcherTimer.Run(() =>
        {
            NotifyCooldown();
            return OnCooldown;
        }, TimeSpan.FromMilliseconds(200));
    }

    public async Task LoadOlderAsync()
    {
        var timeline = _timeline;
        var cancellation = _channelLoad;
        if (timeline is null || cancellation is null || IsChannelLoading) return;
        try
        {
            await timeline.LoadOlderAsync(cancellation.Token);
            if (ReferenceEquals(_timeline, timeline)) SyncMessages();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (ReferenceEquals(_timeline, timeline)) OnError(error);
        }
    }

    private async Task SaveModerationAsync()
    {
        var session = SelectedInstance?.Context.Session ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        var server = ActiveServer ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        var words = ModerationWords.Split(['\r', '\n', ',', '，'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!int.TryParse(CooldownInput.Trim(), out var seconds) || seconds < 0 || seconds > 600)
            throw new InvalidOperationException(_text.Get(TextKey.InvalidCooldown));
        await session.PatchModerationAsync(server.Id, words, seconds, _lifetime);
        Status = _text.Get(TextKey.ModerationSaved);
    }

    public void Dispose()
    {
        DetachTimeline();
        FlushDraft();
        _typingTimer?.Stop();
        DisposePresentation();
        _previews.Dispose();
        ClearRailAvatars();
        _text.PropertyChanged -= OnTextChanged;
        _chrome.Changed -= OnChromeChanged;
        _shortcuts.Changed -= OnShortcutsChanged;
        _osTransport.RaiseRequested -= OnOsRaise;
        ClearPlaybacks();
        Settings.Dispose();
        Wallpaper.Dispose();
        Workspace.Dispose();
        _transport.Dispose();
    }
}

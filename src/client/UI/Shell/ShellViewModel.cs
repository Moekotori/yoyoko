using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Chat.Core.Instances;
using Chat.Localization;
using Chat.Core.Messaging;
using Chat.Core.Realtime;
using Chat.Core.Sessions;
using Chat.Core.Voice;
using Chat.Protocol;
using Chat.UI.Channels;
using Chat.UI.Chat;
using Chat.UI.Components;
using Chat.UI.Instances;
using Chat.UI.Localization;
using Chat.UI.Settings;
using Chat.UI.Workspace;
using ChannelItem = Chat.UI.Channels.ChannelItem;

namespace Chat.UI.Shell;

public sealed class ShellViewModel : ObservableObject, IDisposable
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
    private readonly CancellationToken _lifetime;
    private DateTimeOffset _cooldownUntil;
    private string _moderationWords = "";
    private string _cooldownInput = "0";
    private bool _settingsOpen;
    private readonly SemaphoreSlim _images = new(4);
    private readonly Dictionary<Uri, Bitmap> _bitmaps = [];
    private readonly Dictionary<Guid, (Uri Url, AvatarPlayback Playback)> _playbacks = [];
    private long _bitmapBytes;
    private string _address = "";
    private string _status = "";
    private string _username = "";
    private string _displayName = "";
    private string _password = "";
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
    public ShellViewModel(InstanceManager instances, IMessageCache cache, ICredentialVault vault,
        IInstanceDiscovery discovery, IChatApiFactory apis, Func<IGatewayConnection> gateways,
        IVoiceMedia media, ILocalePreference locale, IChatChrome chrome, I18n text, string productName, CancellationToken lifetime)
    {
        _instances = instances;
        _cache = cache;
        _vault = vault;
        _discovery = discovery;
        _apis = apis;
        _gateways = gateways;
        _media = media;
        _text = text;
        _chrome = chrome;
        ProductName = productName;
        _lifetime = lifetime;
        Settings = new(locale, chrome, () => ShowSettings = false, () => SelectedInstance?.Context.Session, PickAvatarAsync, OnError, text);
        chrome.Changed += OnChromeChanged;
        OpenAddInstance = new(_ =>
        {
            ShowSettings = false;
            SelectedInstance = null;
        });
        OpenSettings = new(_ => { ShowSettings = true; Settings.Profile.Reload(); });
        _text.PropertyChanged += OnTextChanged;
        AddInstance = new(AddAsync, OnError);
        SignIn = new(() => AuthenticateAsync(false), OnError);
        Register = new(() => AuthenticateAsync(true), OnError);
        Send = new(SendAsync, OnError);
        AttachFile = new(AttachAsync, OnError);
        CreateServer = new(CreateServerAsync, OnError);
        JoinServer = new(JoinAsync, OnError);
        SaveModeration = new(SaveModerationAsync, OnError);
        SignOut = new(SignOutAsync, OnError);
        ToggleMute = new(() => SetMuteAsync(true), OnError);
        ToggleDeaf = new(() => SetDeafAsync(true), OnError);
        LeaveVoice = new(LeaveVoiceAsync, OnError);
        BeginAddInstance = new(_ => { SelectedInstance = null; });
        Workspace = new(false, text);
    }
    public string ProductName { get; }
    public WorkspaceViewModel Workspace { get; }
    public bool IsPreview => false;
    public ActionCommand BeginAddInstance { get; }
    public ObservableCollection<InstanceItem> Instances { get; } = [];
    public ObservableCollection<ChannelItem> Channels { get; } = [];
    public ObservableCollection<MessageRow> Messages { get; } = [];
    public AsyncCommand AddInstance { get; }
    public AsyncCommand SignIn { get; }
    public AsyncCommand Register { get; }
    public AsyncCommand Send { get; }
    public AsyncCommand AttachFile { get; }
    public AsyncCommand CreateServer { get; }
    public AsyncCommand JoinServer { get; }
    public AsyncCommand SaveModeration { get; }
    public AsyncCommand SignOut { get; }
    public AsyncCommand ToggleMute { get; }
    public AsyncCommand ToggleDeaf { get; }
    public AsyncCommand LeaveVoice { get; }
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
    public ActionCommand OpenSettings { get; }
    public SettingsViewModel Settings { get; }
    public Func<Task<IReadOnlyList<PickedFile>>>? PickFiles { get; set; }
    public Func<Task<PickedFile?>>? PickAvatar { get; set; }
    public Func<string, Task<Stream?>>? OpenSaveStream { get; set; }
    public Action? ScrollToLatest { get; set; }
    public string Address { get => _address; set { _address = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string Username { get => _username; set { _username = value; Changed(); } }
    public string DisplayName { get => _displayName; set { _displayName = value; Changed(); } }
    public string Password { get => _password; set { _password = value; Changed(); } }
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
            var left = (int)Math.Ceiling((_cooldownUntil - DateTimeOffset.UtcNow).TotalSeconds);
            return Math.Max(0, left);
        }
    }
    public bool OnCooldown => CooldownLeft > 0;
    public bool CanSend => !OnCooldown && (Draft.Trim().Length > 0 || HasPending);
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
            Changed(nameof(ShowChat));
            Changed(nameof(ShowVoice));
            Changed(nameof(ShowSettings));
            Changed(nameof(IsSignedIn));
            Changed(nameof(PaneTitle));
            Changed(nameof(AccountName));
            Changed(nameof(AccountHandle));
            Changed(nameof(AccountPlayback));
            Changed(nameof(InviteHint));
            _ = LoadSessionVisualsAsync();
        }
    }
    public ChannelItem? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            _selectedChannel = value;
            Changed();
            Changed(nameof(PaneTitle));
            Changed(nameof(ShowChat));
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
            Changed(nameof(ShowAddInstance));
            Changed(nameof(ShowAuth));
            Changed(nameof(ShowChat));
            Changed(nameof(ShowVoice));
            Changed(nameof(PaneTitle));
        }
    }
    public bool ShowAddInstance => SelectedInstance is null && !ShowSettings;
    public bool ShowAuth => SelectedInstance is not null && !IsSignedIn && !ShowSettings;
    public bool ShowGuest => !IsSignedIn;
    public bool ShowChat => IsSignedIn && SelectedChannel is { Kind: "text" } && !ShowSettings;
    public bool ShowVoice => IsSignedIn && SelectedChannel is { Kind: "voice" } && !ShowSettings;
    public bool InVoice => SelectedInstance?.Context.Session?.Voice.Joined == true;
    public string MuteLabel => SelectedInstance?.Context.Session?.Voice.SelfMute == true ? _text.Get(TextKey.Unmute) : _text.Get(TextKey.Mute);
    public string DeafLabel => SelectedInstance?.Context.Session?.Voice.SelfDeaf == true ? _text.Get(TextKey.Undeafen) : _text.Get(TextKey.Deafen);
    public string VoiceStatus
    {
        get
        {
            var voice = SelectedInstance?.Context.Session?.Voice;
            if (voice is null) return "";
            if (voice.MediaError is { Length: > 0 } error) return _text.Get(TextKey.VoiceMediaError, error);
            return voice.Joined ? _text.Get(TextKey.VoiceConnected) : _text.Get(TextKey.VoiceJoinHint);
        }
    }
    public IEnumerable<string> VoiceMembers =>
        SelectedChannel is null ? [] :
        SelectedInstance?.Context.Session?.Voice.InChannel(SelectedChannel.Id)
            .Select(state => state.DisplayName
                + (string.IsNullOrEmpty(state.AudioQuality) ? "" : " · " + state.AudioQuality)
                + (state.SelfMute ? _text.Get(TextKey.MutedSuffix) : "")
                + (state.SelfDeaf ? _text.Get(TextKey.DeafenedSuffix) : ""))
        ?? [];
    public string PaneTitle => ShowSettings ? _text.Get(TextKey.Settings)
        : SelectedChannel?.Label ?? (ShowAuth ? _text.Get(TextKey.SignIn) : ProductName);
    public string AccountName => SelectedInstance?.Context.Account?.DisplayName ?? "";
    public string AccountHandle => SelectedInstance?.Context.Account is { Username: { Length: > 0 } name } ? "@" + name : "";
    public AvatarPlayback? AccountPlayback { get; private set; }
    public string InviteHint
    {
        get
        {
            var invite = SelectedInstance?.Context.Session?.Servers.FirstOrDefault()?.InviteCode;
            return string.IsNullOrEmpty(invite) ? _text.Get(TextKey.InviteHintEmpty) : _text.Get(TextKey.InviteHint, invite);
        }
    }
    public ObservableCollection<PendingFileItem> PendingFiles { get; } = [];
    public bool HasPending => PendingFiles.Count > 0;
    public string FileLimitTip
    {
        get
        {
            var session = SelectedInstance?.Context.Session;
            var max = FileKinds.SizeLabel(session?.MaxAttachmentBytes ?? ProtocolVersion.MaxAttachmentBytes);
            var count = session?.MaxAttachments ?? ProtocolVersion.MaxAttachmentsPerMessage;
            return _text.Get(TextKey.FileLimitHint, max, count);
        }
    }

    public async Task InitializeAsync(Func<CancellationToken, Task> initializeCache)
    {
        try
        {
            await initializeCache(_lifetime);
            await _instances.LoadCachedAsync(_lifetime);
            foreach (var item in _instances.Contexts) Instances.Add(new(item));
            SelectedInstance = Instances.FirstOrDefault();
            foreach (var item in Instances.ToList())
                await RestoreAsync(item);
        }
        catch (Exception exception) { Status = _text.Get(TextKey.CacheLoadFailed, exception.Message); }
    }

    private async Task RestoreAsync(InstanceItem item)
    {
        try
        {
            var info = await _discovery.DiscoverAsync(item.Context.Descriptor.BaseUrl, _lifetime);
            var session = await InstanceSession.RestoreAsync(item.Context.Descriptor, _apis.Create(info.Api, info.Gateway),
                _cache, _vault, _gateways, _media, _lifetime);
            if (session is null) return;
            session.ApplyDiscovery(info);
            item.Context.AttachSession(session);
            BindSession(session);
            if (SelectedInstance == item) RefreshCommunity();
        }
        catch { /* offline: stay on login */ }
    }

    private async Task AddAsync()
    {
        Status = _text.Get(TextKey.Discovering);
        var instance = await _instances.AddAsync(Address, _lifetime);
        var item = Instances.FirstOrDefault(item => item.Context.Descriptor.Id == instance.Descriptor.Id);
        if (item is null) { item = new(instance); Instances.Add(item); }
        SelectedInstance = item;
        Status = _text.Get(TextKey.InstanceSaved);
        Address = "";
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
        item.Context.AttachSession(session);
        BindSession(session);
        Password = "";
        Status = "";
        RefreshCommunity();
    }

    private void BindSession(InstanceSession session)
    {
        session.CommunityChanged += () => Dispatcher.UIThread.Post(RefreshCommunity);
        session.Voice.Changed += () => Dispatcher.UIThread.Post(NotifyVoice);
        session.MessageArrived += message => Dispatcher.UIThread.Post(() =>
        {
            _timeline?.ApplyRemote(message);
            SyncMessages();
        });
    }

    private void RefreshCommunity()
    {
        var session = SelectedInstance?.Context.Session;
        Channels.Clear();
        if (session is null)
        {
            SelectedChannel = null;
            NotifySession();
            return;
        }
        foreach (var channel in session.Channels)
            Channels.Add(new(channel.Id, channel.ServerId, channel.Name, channel.Kind, channel.AudioQuality));
        if (SelectedChannel is null || Channels.All(item => item.Id != SelectedChannel.Id))
            SelectedChannel = Channels.FirstOrDefault();
        var server = ActiveServer;
        ModerationWords = server?.BlockedWords is { Length: > 0 } words
            ? string.Join(Environment.NewLine, words)
            : "";
        CooldownInput = (server?.CooldownSeconds ?? 0).ToString();
        NotifySession();
    }

    private void NotifySession()
    {
        Changed(nameof(IsSignedIn));
        Changed(nameof(ShowAuth));
        Changed(nameof(ShowGuest));
        Changed(nameof(ShowChat));
        Changed(nameof(AccountName));
        Changed(nameof(AccountHandle));
        Changed(nameof(AccountPlayback));
        Changed(nameof(InviteHint));
        Changed(nameof(PaneTitle));
        Changed(nameof(ShowSettings));
        if (SelectedInstance?.Context.Session is { } session)
            _ = LoadUserAvatarAsync(session.Me.Id);
        else
            ClearPlaybacks();
        NotifyVoice();
        Changed(nameof(CanModerate));
        Changed(nameof(ActiveServer));
        Changed(nameof(CanSend));
    }

    private void OnTextChanged(object? sender, PropertyChangedEventArgs args) => OnLocaleChanged();
    private void OnLocaleChanged()
    {
        foreach (var channel in Channels) channel.Refresh();
        Changed(nameof(InstanceName));
        Changed(nameof(InstanceHost));
        Changed(nameof(PaneTitle));
        Changed(nameof(InviteHint));
        Changed(nameof(MuteLabel));
        Changed(nameof(DeafLabel));
        Changed(nameof(VoiceStatus));
        Changed(nameof(VoiceMembers));
        Changed(nameof(FileLimitTip));
        RebuildQualityChoices();
    }

    private void NotifyVoice()
    {
        Changed(nameof(ShowVoice));
        Changed(nameof(ShowChat));
        Changed(nameof(InVoice));
        Changed(nameof(MuteLabel));
        Changed(nameof(DeafLabel));
        Changed(nameof(VoiceStatus));
        Changed(nameof(VoiceMembers));
        RebuildQualityChoices();
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
        var current = SelectedInstance?.Context.Session?.Voice.Quality ?? AudioQualities.Studio;
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

    private async Task LoadSessionVisualsAsync()
    {
        RefreshCommunity();
        if (SelectedChannel is not null) await OpenChannelAsync();
    }

    private async Task OpenChannelAsync()
    {
        var session = SelectedInstance?.Context.Session;
        DetachTimeline();
        if (session is null || SelectedChannel is null)
        {
            Messages.Clear();
            return;
        }
        if (SelectedChannel.Kind == "voice")
        {
            Messages.Clear();
            await session.Voice.JoinAsync(SelectedChannel.Id, SelectedAudioQuality?.Id, _lifetime);
            NotifyVoice();
            return;
        }
        _timeline = session.OpenChannel(SelectedChannel.Id);
        _timelineChanged = () => Dispatcher.UIThread.Post(SyncMessages);
        _timeline.Changed += _timelineChanged;
        await _timeline.LoadLatestAsync(_lifetime);
        SyncMessages();
        ScrollToLatest?.Invoke();
    }

    private void DetachTimeline()
    {
        if (_timeline is not null && _timelineChanged is not null)
            _timeline.Changed -= _timelineChanged;
        _timeline = null;
        _timelineChanged = null;
    }

    private void SyncMessages()
    {
        if (_timeline is null) { Messages.Clear(); return; }
        var session = SelectedInstance?.Context.Session;
        var seen = new HashSet<Guid>();
        var added = false;
        foreach (var item in _timeline.Items)
        {
            seen.Add(item.LocalId);
            var row = Messages.FirstOrDefault(existing => existing.Item.LocalId == item.LocalId);
            if (row is null)
            {
                row = new(item, session?.AuthorName(item.Message.AuthorId) ?? "", SaveAttachmentAsync);
                Messages.Add(row);
                added = true;
            }
            else
            {
                row.Update();
                row.SetAuthor(session?.AuthorName(item.Message.AuthorId) ?? row.Author);
            }
            if (_playbacks.TryGetValue(item.Message.AuthorId, out var cached))
                row.Playback = cached.Playback;
            _ = LoadAttachmentsAsync(row);
            if (session is not null) _ = LoadUserAvatarAsync(item.Message.AuthorId);
        }
        for (var i = Messages.Count - 1; i >= 0; i--)
            if (!seen.Contains(Messages[i].Item.LocalId))
                Messages.RemoveAt(i);
        TrimBitmaps();
        if (added) ScrollToLatest?.Invoke();
    }

    private async Task LoadAttachmentsAsync(MessageRow row)
    {
        var session = SelectedInstance?.Context.Session;
        if (session is null) return;
        foreach (var file in row.Files)
        {
            if (file.HasPreview || file.PreviewUrl is null) continue;
            if (_bitmaps.TryGetValue(file.PreviewUrl, out var cached))
            {
                file.Preview = cached;
                continue;
            }
            await _images.WaitAsync(_lifetime);
            try
            {
                var bytes = await session.DownloadAsync(file.PreviewUrl, 512 * 1024, _lifetime);
                if (bytes is null) continue;
                using var stream = new MemoryStream(bytes);
                var bitmap = Bitmap.DecodeToWidth(stream, 128);
                _bitmaps[file.PreviewUrl] = bitmap;
                _bitmapBytes += (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
                file.Preview = bitmap;
                TrimBitmaps();
            }
            catch { /* chip remains without preview */ }
            finally { _images.Release(); }
        }
    }

    private void TrimBitmaps()
    {
        while (_bitmapBytes > MemoryBudget.ThumbnailBytes && _bitmaps.Count > 0)
        {
            var oldest = _bitmaps.First();
            _bitmapBytes -= (long)oldest.Value.PixelSize.Width * oldest.Value.PixelSize.Height * 4;
            if (!Messages.SelectMany(row => row.Files).Any(file => file.Preview == oldest.Value)) oldest.Value.Dispose();
            _bitmaps.Remove(oldest.Key);
        }
    }

    private async Task SendAsync()
    {
        if (_timeline is null) return;
        var text = Draft.Trim();
        if (text.Length == 0 && PendingFiles.Count == 0) return;
        var files = PendingFiles.Select(item => item.File).ToList();
        foreach (var file in files)
            if (file.Content.CanSeek) file.Content.Position = 0;
        Draft = "";
        Status = "";
        try
        {
            await _timeline.SendAsync(text.Length == 0 ? null : text, files, _lifetime);
            foreach (var item in PendingFiles.ToList())
                await item.File.DisposeAsync();
            PendingFiles.Clear();
            NotifyPending();
            ScrollToLatest?.Invoke();
        }
        catch
        {
            foreach (var file in files)
                if (file.Content.CanSeek) file.Content.Position = 0;
            throw;
        }
    }

    public async Task QueueFilesAsync(IReadOnlyList<PickedFile> files)
    {
        var session = SelectedInstance?.Context.Session;
        var max = session?.MaxAttachmentBytes ?? ProtocolVersion.MaxAttachmentBytes;
        var cap = session?.MaxAttachments ?? ProtocolVersion.MaxAttachmentsPerMessage;
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
        if (PickFiles is null) return;
        await QueueFilesAsync(await PickFiles());
    }

    private void NotifyPending()
    {
        Changed(nameof(HasPending));
        Changed(nameof(FileLimitTip));
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
        await session.CreateServerAsync(name, _lifetime);
        CommunityName = "";
        RefreshCommunity();
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
        if (item.Context.Session is { } session) await session.SignOutAsync();
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

    private Task SetChannelQualityAsync(string quality)
    {
        var session = SelectedInstance?.Context.Session ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        var channel = SelectedChannel ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        return session.Voice.SetChannelMaxQualityAsync(channel.Id, quality, _lifetime);
    }

    private async Task<PickedFile?> PickAvatarAsync() =>
        PickAvatar is null ? null : await PickAvatar();

    private async Task LoadUserAvatarAsync(Guid userId)
    {
        var session = SelectedInstance?.Context.Session;
        var user = session?.User(userId);
        if (user?.Avatar is null)
        {
            if (_playbacks.Remove(userId, out var stale)) stale.Playback.Dispose();
            ApplyPlayback(userId, null);
            return;
        }
        var url = user.Avatar.Animated
            ? user.Avatar.DownloadUrl
            : user.Avatar.ThumbnailUrl ?? user.Avatar.DownloadUrl;
        if (_playbacks.TryGetValue(userId, out var existing) && existing.Url == url)
        {
            ApplyPlayback(userId, existing.Playback);
            return;
        }
        var limit = user.Avatar.Animated
            ? (int)Math.Min(Math.Max(user.Avatar.Size, 1) + 65_536, ProtocolVersion.MaxAvatarBytes)
            : 512 * 1024;
        await _images.WaitAsync(_lifetime);
        try
        {
            var bytes = await session!.DownloadAsync(url, limit, _lifetime);
            if (bytes is null || bytes.Length == 0) return;
            var playback = AvatarPlayback.Decode(bytes, 128);
            if (_playbacks.Remove(userId, out var previous)) previous.Playback.Dispose();
            _playbacks[userId] = (url, playback);
            ApplyPlayback(userId, playback);
        }
        catch { /* keep letter avatar */ }
        finally { _images.Release(); }
    }

    private void ApplyPlayback(Guid userId, AvatarPlayback? playback)
    {
        if (SelectedInstance?.Context.Session?.Me.Id == userId)
        {
            AccountPlayback = playback;
            Changed(nameof(AccountPlayback));
            Settings.Profile.SetPlayback(playback);
        }
        foreach (var row in Messages)
            if (row.Item.Message.AuthorId == userId)
                row.Playback = playback;
    }

    private void ClearPlaybacks()
    {
        foreach (var item in _playbacks.Values) item.Playback.Dispose();
        _playbacks.Clear();
        AccountPlayback = null;
        Changed(nameof(AccountPlayback));
        Settings.Profile.SetPlayback(null);
    }

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

    private void OnChromeChanged()
    {
        Changed(nameof(EnterToSend));
        Changed(nameof(CompactLayout));
        Changed(nameof(ReduceMotion));
    }

    private void BeginCooldown(int seconds)
    {
        if (seconds <= 0) return;
        _cooldownUntil = DateTimeOffset.UtcNow.AddSeconds(seconds);
        Changed(nameof(CooldownLeft));
        Changed(nameof(OnCooldown));
        Changed(nameof(CanSend));
        DispatcherTimer.Run(() =>
        {
            Changed(nameof(CooldownLeft));
            Changed(nameof(OnCooldown));
            Changed(nameof(CanSend));
            return OnCooldown;
        }, TimeSpan.FromMilliseconds(200));
    }

    public async Task LoadOlderAsync()
    {
        if (_timeline is null) return;
        await _timeline.LoadOlderAsync(_lifetime);
        SyncMessages();
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
        _text.PropertyChanged -= OnTextChanged;
        _chrome.Changed -= OnChromeChanged;
        ClearPlaybacks();
        Settings.Dispose();
        Workspace.Dispose();
    }
}

public sealed record AudioQualityChoice(string Id, string Title, string Detail)
{
    public string Label => Title + "  " + Detail;
}

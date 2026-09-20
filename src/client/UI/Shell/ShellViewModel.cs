using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Chat.Core.Instances;
using Chat.Core.Messaging;
using Chat.Core.Realtime;
using Chat.Core.Sessions;
using Chat.Protocol;
using Chat.UI.Channels;
using Chat.UI.Chat;
using Chat.UI.Components;
using Chat.UI.Instances;

namespace Chat.UI.Shell;

public sealed class ShellViewModel : ObservableObject
{
    private readonly InstanceManager _instances;
    private readonly IMessageCache _cache;
    private readonly ICredentialVault _vault;
    private readonly IInstanceDiscovery _discovery;
    private readonly IChatApiFactory _apis;
    private readonly Func<IGatewayConnection> _gateways;
    private readonly CancellationToken _lifetime;
    private readonly SemaphoreSlim _images = new(4);
    private readonly Dictionary<Uri, Bitmap> _bitmaps = [];
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
    public ShellViewModel(InstanceManager instances, IMessageCache cache, ICredentialVault vault,
        IInstanceDiscovery discovery, IChatApiFactory apis, Func<IGatewayConnection> gateways,
        string productName, CancellationToken lifetime)
    {
        _instances = instances;
        _cache = cache;
        _vault = vault;
        _discovery = discovery;
        _apis = apis;
        _gateways = gateways;
        ProductName = productName;
        _lifetime = lifetime;
        AddInstance = new(AddAsync, OnError);
        SignIn = new(() => AuthenticateAsync(false), OnError);
        Register = new(() => AuthenticateAsync(true), OnError);
        Send = new(SendAsync, OnError);
        AttachImage = new(AttachAsync, OnError);
        CreateServer = new(CreateServerAsync, OnError);
        JoinServer = new(JoinAsync, OnError);
        SignOut = new(SignOutAsync, OnError);
    }
    public string ProductName { get; }
    public ObservableCollection<InstanceItem> Instances { get; } = [];
    public ObservableCollection<ChannelItem> Channels { get; } = [];
    public ObservableCollection<MessageRow> Messages { get; } = [];
    public AsyncCommand AddInstance { get; }
    public AsyncCommand SignIn { get; }
    public AsyncCommand Register { get; }
    public AsyncCommand Send { get; }
    public AsyncCommand AttachImage { get; }
    public AsyncCommand CreateServer { get; }
    public AsyncCommand JoinServer { get; }
    public AsyncCommand SignOut { get; }
    public Func<Task<IReadOnlyList<PickedImage>>>? PickImages { get; set; }
    public Action? ScrollToLatest { get; set; }
    public string Address { get => _address; set { _address = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string Username { get => _username; set { _username = value; Changed(); } }
    public string DisplayName { get => _displayName; set { _displayName = value; Changed(); } }
    public string Password { get => _password; set { _password = value; Changed(); } }
    public string Draft { get => _draft; set { _draft = value; Changed(); } }
    public string CommunityName { get => _communityName; set { _communityName = value; Changed(); } }
    public string InviteInput { get => _inviteInput; set { _inviteInput = value; Changed(); } }
    public InstanceItem? SelectedInstance
    {
        get => _selected;
        set
        {
            _selected = value;
            Changed();
            Changed(nameof(InstanceName));
            Changed(nameof(InstanceHost));
            Changed(nameof(ShowAddInstance));
            Changed(nameof(ShowAuth));
            Changed(nameof(ShowChat));
            Changed(nameof(IsSignedIn));
            Changed(nameof(PaneTitle));
            Changed(nameof(AccountName));
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
    public string InstanceName => SelectedInstance?.Name ?? "工作空间";
    public string InstanceHost => SelectedInstance?.Host ?? "尚未连接实例";
    public bool IsSignedIn => SelectedInstance?.Context.Session is not null;
    public bool ShowAddInstance => SelectedInstance is null;
    public bool ShowAuth => SelectedInstance is not null && !IsSignedIn;
    public bool ShowGuest => !IsSignedIn;
    public bool ShowChat => IsSignedIn && SelectedChannel is not null;
    public string PaneTitle => SelectedChannel?.Label ?? (ShowAuth ? "登录" : ProductName);
    public string AccountName => SelectedInstance?.Context.Account?.DisplayName ?? "";
    public string InviteHint
    {
        get
        {
            var invite = SelectedInstance?.Context.Session?.Servers.FirstOrDefault()?.InviteCode;
            return string.IsNullOrEmpty(invite) ? "创建或加入一个社区后即可聊天。" : "邀请码 " + invite;
        }
    }
    public List<PickedImage> PendingImages { get; } = [];

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
        catch (Exception exception) { Status = "本地缓存加载失败：" + exception.Message; }
    }

    private async Task RestoreAsync(InstanceItem item)
    {
        try
        {
            var info = await _discovery.DiscoverAsync(item.Context.Descriptor.BaseUrl, _lifetime);
            var session = await InstanceSession.RestoreAsync(item.Context.Descriptor, _apis.Create(info.Api, info.Gateway),
                _cache, _vault, _gateways, _lifetime);
            if (session is null) return;
            item.Context.AttachSession(session);
            BindSession(session);
            if (SelectedInstance == item) RefreshCommunity();
        }
        catch { /* offline: stay on login */ }
    }

    private async Task AddAsync()
    {
        Status = "正在发现实例…";
        var instance = await _instances.AddAsync(Address, _lifetime);
        var item = Instances.FirstOrDefault(item => item.Context.Descriptor.Id == instance.Descriptor.Id);
        if (item is null) { item = new(instance); Instances.Add(item); }
        SelectedInstance = item;
        Status = "已保存实例。请登录或注册。";
        Address = "";
    }

    private async Task AuthenticateAsync(bool register)
    {
        var item = SelectedInstance ?? throw new InvalidOperationException("请先添加实例。");
        Status = register ? "正在注册…" : "正在登录…";
        var info = await _discovery.DiscoverAsync(item.Context.Descriptor.BaseUrl, _lifetime);
        var session = await InstanceSession.SignInAsync(item.Context.Descriptor, _apis.Create(info.Api, info.Gateway),
            _cache, _vault, _gateways, Username.Trim(), Password, string.IsNullOrWhiteSpace(DisplayName) ? Username.Trim() : DisplayName.Trim(),
            register, _lifetime);
        item.Context.AttachSession(session);
        BindSession(session);
        Password = "";
        Status = "";
        RefreshCommunity();
    }

    private void BindSession(InstanceSession session)
    {
        session.CommunityChanged += () => Dispatcher.UIThread.Post(RefreshCommunity);
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
        foreach (var channel in session.Channels.Where(channel => channel.Kind == "text"))
            Channels.Add(new(channel.Id, channel.Name, channel.Kind));
        if (SelectedChannel is null || Channels.All(item => item.Id != SelectedChannel.Id))
            SelectedChannel = Channels.FirstOrDefault();
        NotifySession();
    }

    private void NotifySession()
    {
        Changed(nameof(IsSignedIn));
        Changed(nameof(ShowAuth));
        Changed(nameof(ShowGuest));
        Changed(nameof(ShowChat));
        Changed(nameof(AccountName));
        Changed(nameof(InviteHint));
        Changed(nameof(PaneTitle));
    }

    private async Task LoadSessionVisualsAsync()
    {
        RefreshCommunity();
        if (SelectedChannel is not null) await OpenChannelAsync();
    }

    private async Task OpenChannelAsync()
    {
        var session = SelectedInstance?.Context.Session;
        if (session is null || SelectedChannel is null)
        {
            Messages.Clear();
            _timeline = null;
            return;
        }
        _timeline = session.OpenChannel(SelectedChannel.Id);
        _timeline.Changed += () => Dispatcher.UIThread.Post(SyncMessages);
        await _timeline.LoadLatestAsync(_lifetime);
        SyncMessages();
        ScrollToLatest?.Invoke();
    }

    private void SyncMessages()
    {
        if (_timeline is null) { Messages.Clear(); return; }
        var session = SelectedInstance?.Context.Session;
        var seen = new HashSet<Guid>();
        foreach (var item in _timeline.Items)
        {
            seen.Add(item.LocalId);
            var row = Messages.FirstOrDefault(existing => existing.Item.LocalId == item.LocalId);
            if (row is null)
            {
                row = new(item, session?.AuthorName(item.Message.AuthorId) ?? "");
                Messages.Add(row);
                _ = LoadImageAsync(row);
            }
            else row.Update();
        }
        for (var i = Messages.Count - 1; i >= 0; i--)
            if (!seen.Contains(Messages[i].Item.LocalId))
            {
                Messages[i].Image?.Dispose();
                Messages.RemoveAt(i);
            }
        TrimBitmaps();
        ScrollToLatest?.Invoke();
    }

    private async Task LoadImageAsync(MessageRow row)
    {
        if (row.ImageUrl is null) return;
        if (_bitmaps.TryGetValue(row.ImageUrl, out var cached))
        {
            row.Image = cached;
            return;
        }
        var session = SelectedInstance?.Context.Session;
        if (session is null) return;
        await _images.WaitAsync(_lifetime);
        try
        {
            var bytes = await session.DownloadAsync(row.ImageUrl, 512 * 1024, _lifetime);
            if (bytes is null) return;
            using var stream = new MemoryStream(bytes);
            var bitmap = Bitmap.DecodeToWidth(stream, 256);
            _bitmaps[row.ImageUrl] = bitmap;
            _bitmapBytes += (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
            row.Image = bitmap;
            TrimBitmaps();
        }
        catch { /* placeholder remains */ }
        finally { _images.Release(); }
    }

    private void TrimBitmaps()
    {
        while (_bitmapBytes > MemoryBudget.ThumbnailBytes && _bitmaps.Count > 0)
        {
            var oldest = _bitmaps.First();
            _bitmapBytes -= (long)oldest.Value.PixelSize.Width * oldest.Value.PixelSize.Height * 4;
            if (!Messages.Any(row => row.Image == oldest.Value)) oldest.Value.Dispose();
            _bitmaps.Remove(oldest.Key);
        }
    }

    private async Task SendAsync()
    {
        if (_timeline is null) return;
        var text = Draft.Trim();
        var images = PendingImages.ToList();
        if (text.Length == 0 && images.Count == 0) return;
        Draft = "";
        PendingImages.Clear();
        Status = "";
        try
        {
            await _timeline.SendAsync(text.Length == 0 ? null : text, images, _lifetime);
            ScrollToLatest?.Invoke();
        }
        finally
        {
            foreach (var image in images) await image.DisposeAsync();
        }
    }

    private async Task AttachAsync()
    {
        if (PickImages is null) return;
        foreach (var image in await PickImages())
        {
            if (PendingImages.Count >= 4) { await image.DisposeAsync(); continue; }
            PendingImages.Add(image);
        }
        Status = PendingImages.Count == 0 ? "" : $"已选择 {PendingImages.Count} 张图片";
    }

    private async Task CreateServerAsync()
    {
        var session = SelectedInstance?.Context.Session ?? throw new InvalidOperationException("请先登录。");
        var name = string.IsNullOrWhiteSpace(CommunityName) ? "Community" : CommunityName.Trim();
        await session.CreateServerAsync(name, _lifetime);
        CommunityName = "";
        RefreshCommunity();
    }

    private async Task JoinAsync()
    {
        var session = SelectedInstance?.Context.Session ?? throw new InvalidOperationException("请先登录。");
        await session.JoinAsync(InviteInput, _lifetime);
        InviteInput = "";
        RefreshCommunity();
    }

    private async Task SignOutAsync()
    {
        var item = SelectedInstance ?? throw new InvalidOperationException("没有选中的实例。");
        if (item.Context.Session is { } session) await session.SignOutAsync();
        item.Context.AttachSessionClear();
        Channels.Clear();
        Messages.Clear();
        SelectedChannel = null;
        NotifySession();
        Changed(nameof(ShowAuth));
        Changed(nameof(ShowGuest));
        Changed(nameof(IsSignedIn));
    }

    private void OnError(Exception exception) =>
        Status = exception is OperationCanceledException ? "已取消。" : exception.Message;
}

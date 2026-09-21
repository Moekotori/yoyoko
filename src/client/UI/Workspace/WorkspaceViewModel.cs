using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;

namespace Chat.UI.Workspace;

// Presentation fixtures are opt-in, never persisted or passed to the Core/network layers.
public sealed class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly List<Bitmap> _images = [];
    private ChannelItem? _selected;
    private bool _membersVisible = true;
    private bool _textExpanded = true;
    private bool _voiceExpanded = true;
    private bool _searchVisible;
    private string _query = "";
    private string _draft = "";
    private string _notice = "";
    private readonly I18n _text;
    public WorkspaceViewModel(bool isPreview, I18n? text = null)
    {
        _text = text ?? global::Chat.UI.Localization.I18n.Presenter;
        IsPreview = isPreview;
        SelectChannel = new(value => { if (value is ChannelItem channel) SelectedChannel = channel; });
        CloseTab = new(value => { if (value is ChannelItem channel) CloseChannel(channel); });
        Send = new(_ => { if (CanAttemptSend) Notice = _text.Get(TextKey.NotImplemented); });
        Escape = new(_ => { if (HasNotice) Notice = ""; else if (SearchVisible) { SearchVisible = false; Query = ""; } });
        ToggleMembers = new(_ => { MembersVisible = !MembersVisible; });
        ToggleText = new(_ => { TextExpanded = !TextExpanded; });
        ToggleVoice = new(_ => { VoiceExpanded = !VoiceExpanded; });
        ToggleSearch = new(_ => { SearchVisible = !SearchVisible; Query = ""; });
        Unavailable = new(_ => Notice = _text.Get(TextKey.NotImplemented));
        DismissNotice = new(_ => Notice = "");
        _text.PropertyChanged += OnLocaleChanged;
        if (isPreview) LoadPreview();
    }
    public bool IsPreview { get; }
    public string AccountName => IsPreview ? "林" : _text.Get(TextKey.SignedOut);
    public string AccountInitial => Avatar.FromName(AccountName);
    public string AccountStatus => IsPreview ? _text.Get(TextKey.DesignPreview) : _text.Get(TextKey.Offline);
    public Bitmap? AccountAvatar => Members.FirstOrDefault()?.Avatar;
    public Bitmap? VoiceAvatar => Members.Skip(1).FirstOrDefault()?.Avatar;
    public ObservableCollection<ChannelItem> Channels { get; } = [];
    public ObservableCollection<MemberItem> Members { get; } = [];
    public ObservableCollection<PreviewServerItem> PreviewServers { get; } = [];
    public ObservableCollection<MessageItem> Messages { get; } = [];
    public ObservableCollection<ChannelItem> OpenTabs { get; } = [];
    public ActionCommand CloseTab { get; }
    public ActionCommand Send { get; }
    public ActionCommand Escape { get; }
    public ActionCommand SelectChannel { get; }
    public ActionCommand ToggleMembers { get; }
    public ActionCommand ToggleText { get; }
    public ActionCommand ToggleVoice { get; }
    public ActionCommand ToggleSearch { get; }
    public ActionCommand Unavailable { get; }
    public ActionCommand DismissNotice { get; }
    public ChannelItem? SelectedChannel
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            if (_selected is not null) _selected.IsSelected = false;
            _selected = value;
            if (value is not null)
            {
                value.IsSelected = true;
                if (!OpenTabs.Contains(value)) OpenTabs.Add(value);
            }
            _draft = value?.Draft ?? "";
            Changed(nameof(Draft)); Changed(nameof(CanAttemptSend)); Changed(nameof(HasChannel));
            Changed(); Changed(nameof(ChannelName)); Changed(nameof(ComposerPlaceholder)); Changed(nameof(EmptyTitle)); Changed(nameof(EmptyDetail));
            RefreshMessages();
        }
    }
    public string ChannelName => SelectedChannel?.Name ?? _text.Get(TextKey.ChannelFallback);
    public string ComposerPlaceholder => SelectedChannel is null ? _text.Get(TextKey.SelectChannel) : _text.Get(TextKey.MessageToChannel, ChannelName);
    public string MemberHeading => IsPreview ? _text.Get(TextKey.OnlineCount, 3) : _text.Get(TextKey.NotSignedIn);
    public bool HasChannel => SelectedChannel is not null;
    public bool CanAttemptSend => HasChannel && !string.IsNullOrWhiteSpace(Draft);
    public string EmptyTitle => Query.Length > 0 ? _text.Get(TextKey.NoMatchingMessages) : HasChannel ? _text.Get(TextKey.StillQuiet) : _text.Get(TextKey.SelectChannel);
    public string EmptyDetail => Query.Length > 0 ? Query : HasChannel ? "# " + ChannelName : "";
    private void CloseChannel(ChannelItem channel)
    {
        var index = OpenTabs.IndexOf(channel);
        if (index < 0) return;
        OpenTabs.Remove(channel);
        if (SelectedChannel == channel)
            SelectedChannel = OpenTabs.Count > 0 ? OpenTabs[Math.Min(index, OpenTabs.Count - 1)] : null;
    }
    public bool MembersVisible { get => _membersVisible; private set { _membersVisible = value; Changed(); } }
    public bool TextExpanded { get => _textExpanded; private set { _textExpanded = value; Changed(); } }
    public bool VoiceExpanded { get => _voiceExpanded; private set { _voiceExpanded = value; Changed(); } }
    public bool SearchVisible { get => _searchVisible; private set { _searchVisible = value; Changed(); } }
    public string Query { get => _query; set { _query = value; Changed(); RefreshMessages(); } }
    public string Draft { get => _draft; set { _draft = value; if (_selected is not null) _selected.Draft = value; Changed(); Changed(nameof(CanAttemptSend)); } }
    public string Notice { get => _notice; private set { _notice = value; Changed(); Changed(nameof(HasNotice)); } }
    public void ShowNotice(string message) => Notice = message;
    public bool HasNotice => Notice.Length > 0;
    public bool HasNoMessages => Messages.Count == 0;
    private void RefreshMessages()
    {
        Messages.Clear();
        if (_selected is not null)
            foreach (var message in _selected.Messages.Where(m => string.IsNullOrWhiteSpace(Query) || m.Text.Contains(Query, StringComparison.OrdinalIgnoreCase) || m.Name.Contains(Query, StringComparison.OrdinalIgnoreCase)))
                Messages.Add(message);
        Changed(nameof(HasNoMessages)); Changed(nameof(EmptyTitle)); Changed(nameof(EmptyDetail));
    }
    private Bitmap LoadImage(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://Chat.UI/Assets/{name}.png"));
        var image = Bitmap.DecodeToWidth(stream, 128);
        _images.Add(image);
        return image;
    }
    private void LoadPreview()
    {
        var forest = LoadImage("forest");
        var mika = LoadImage("mika");
        var chen = LoadImage("chen");
        PreviewServers.Add(new("林间", forest, true));
        PreviewServers.Add(new("城市", LoadImage("city"), false));
        PreviewServers.Add(new("绿洲", LoadImage("plant"), false));
        Members.Add(new("林", forest)); Members.Add(new("Mika", mika)); Members.Add(new("陈默", chen));
        Channels.Add(new("日常", [
            new("林", "19:20", "下班了，来这里放空一下。", forest),
            new("Mika", "19:22", "今天的晚风很舒服。", mika, true),
            new("林", "19:23", "我也刚回来。", forest),
            new("陈默", "19:26", "新的设计整理好了，发在隔壁频道。", chen),
            new("Mika", "19:28", "我去看看！", mika)
        ]));
        Channels.Add(new("设计", [new("陈默", "19:26", "新的设计整理好了，一起看看。", chen)]));
        Channels.Add(new("随手分享", []));
        OpenTabs.Add(Channels[0]); OpenTabs.Add(Channels[1]);
        SelectedChannel = Channels[0];
    }
    private void OnLocaleChanged(object? sender, PropertyChangedEventArgs args)
    {
        Changed(nameof(AccountName));
        Changed(nameof(AccountInitial));
        Changed(nameof(AccountStatus));
        Changed(nameof(ChannelName));
        Changed(nameof(ComposerPlaceholder));
        Changed(nameof(MemberHeading));
        Changed(nameof(EmptyTitle));
    }
    public void Dispose()
    {
        _text.PropertyChanged -= OnLocaleChanged;
        foreach (var image in _images) image.Dispose();
        _images.Clear();
    }
}

public sealed class ChannelItem(string name, IReadOnlyList<MessageItem> messages) : ObservableObject
{
    private bool _selected;
    public string Name { get; } = name;
    public string Draft { get; set; } = "";
    public IReadOnlyList<MessageItem> Messages { get; } = messages;
    public bool IsSelected { get => _selected; set { _selected = value; Changed(); } }
}
public sealed record PreviewServerItem(string Name, Bitmap Avatar, bool IsSelected);
public sealed record MemberItem(string Name, Bitmap Avatar)
{
    public string Initial => global::Chat.UI.Components.Avatar.FromName(Name);
}
public sealed record MessageItem(string Name, string Time, string Text, Bitmap Avatar, bool HasReaction = false);

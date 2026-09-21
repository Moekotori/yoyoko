using Chat.Protocol;
using Chat.UI.Chat;
using Chat.UI.Components;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    private readonly Dictionary<Guid, (Uri Url, AvatarPlayback Playback)> _playbacks = [];
    private readonly Dictionary<Guid, (Uri Url, AvatarPlayback Playback)> _banners = [];
    private readonly Dictionary<Guid, Uri> _avatarAttempts = [];
    private readonly Dictionary<Guid, Uri> _bannerAttempts = [];
    private CancellationTokenSource? _avatarScope;
    private int _avatarGeneration;
    private int _avatarLoads;
    private int _bannerLoads;
    private bool _clearingAvatars;
    private long AvatarBudget => _visualBudget.AvatarBytes;
    private long BannerBudget => _visualBudget.BannerBytes;

    private void RefreshAvatars()
    {
        if (_clearingAvatars || _visualBudget.Suspended || _presentationDisposed || _lifetime.IsCancellationRequested) return;
        var session = SelectedInstance?.Context.Session;
        if (session is null) return;
        var needed = Messages.Select(row => row.Item.Message.AuthorId).Append(session.Me.Id).ToHashSet();
        foreach (var id in _playbacks.Keys.Where(id => !needed.Contains(id)).ToArray())
        {
            ApplyPlayback(id, null);
            _playbacks[id].Playback.Dispose();
            _playbacks.Remove(id);
        }
        foreach (var id in _avatarAttempts.Keys.Where(id => !needed.Contains(id)).ToArray()) _avatarAttempts.Remove(id);
        _ = LoadUserAvatarAsync(session.Me.Id);
        _ = LoadUserBannerAsync(session.Me.Id);
        foreach (var id in needed)
        {
            if (_avatarLoads >= _visualBudget.Downloads) break;
            _ = LoadUserAvatarAsync(id);
        }
    }

    public void RequestBanner(MemberProfile member) => _ = LoadUserBannerAsync(member.Id);

    private async Task LoadUserAvatarAsync(Guid userId)
    {
        if (_visualBudget.Suspended || _presentationDisposed || _lifetime.IsCancellationRequested) return;
        var session = SelectedInstance?.Context.Session;
        var user = session?.User(userId);
        if (user?.Avatar is null)
        {
            _avatarAttempts.Remove(userId);
            ApplyPlayback(userId, null);
            if (_playbacks.Remove(userId, out var stale)) stale.Playback.Dispose();
            return;
        }
        var animated = user.Avatar.Animated && _visualBudget.Animate;
        var url = animated ? user.Avatar.DownloadUrl : user.Avatar.ThumbnailUrl ?? user.Avatar.DownloadUrl;
        if (_playbacks.TryGetValue(userId, out var existing) && existing.Url == url)
        {
            ApplyPlayback(userId, existing.Playback);
            return;
        }
        if (_avatarLoads >= _visualBudget.Downloads
            || _avatarAttempts.TryGetValue(userId, out var attempted) && attempted == url) return;
        _avatarAttempts[userId] = url;
        if (_playbacks.Values.Sum(item => item.Playback.DecodedBytes) + 128 * 128 * 4 > AvatarBudget) return;
        _avatarLoads++;
        var generation = _avatarGeneration;
        _avatarScope ??= CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        var token = _avatarScope.Token;
        var limit = url == user.Avatar.DownloadUrl
            ? (int)Math.Min(Math.Max(user.Avatar.Size, 1) + 65_536, ProtocolVersion.MaxAvatarBytes) : 512 * 1024;
        try
        {
            var bytes = await session!.DownloadAsync(url, limit, token);
            if (bytes is null || bytes.Length == 0 || token.IsCancellationRequested || generation != _avatarGeneration
                || !ReferenceEquals(session, SelectedInstance?.Context.Session) || _visualBudget.Suspended) return;
            var currentAvatar = session.User(userId)?.Avatar;
            if (currentAvatar is null || currentAvatar.Id != user.Avatar.Id
                || !_avatarAttempts.TryGetValue(userId, out var requested) || requested != url) return;
            if (session.Me.Id != userId && !Messages.Any(row => row.Item.Message.AuthorId == userId)) return;
            var retained = _playbacks.Values.Sum(item => item.Playback.DecodedBytes);
            if (retained + 128 * 128 * 4 > AvatarBudget) return;
            // Reserve the maximum 48 decoded frames before allowing an animated avatar.
            var playback = AvatarPlayback.Decode(bytes, 128,
                animated && retained + 48 * 128 * 128 * 4 <= AvatarBudget);
            ApplyPlayback(userId, null);
            if (_playbacks.Remove(userId, out var previous)) previous.Playback.Dispose();
            _playbacks[userId] = (url, playback);
            ApplyPlayback(userId, playback);
            if (session.Me.Id == userId && SelectedInstance is { } instance)
            {
                // At most 512 KiB of static rail thumbnails, with no extra downloads.
                if (instance.Playback is null && Instances.Count(item => item.Playback is not null) >= 32)
                    Instances.First(item => item.Playback is not null).ClearAvatar();
                instance.SetAvatar(bytes);
            }
        }
        catch { /* Leave the letter avatar; retry on URL or residency change. */ }
        finally
        {
            _avatarLoads--;
            RefreshAvatars();
        }
    }

    private void ApplyPlayback(Guid userId, AvatarPlayback? playback)
    {
        if (SelectedInstance?.Context.Session?.Me.Id == userId)
        {
            AccountPlayback = playback;
            Changed(nameof(AccountPlayback));
            Changed(nameof(HasAvatar));
            Settings.Profile.SetPlayback(playback);
        }
        foreach (var row in Messages)
            if (row.Item.Message.AuthorId == userId) row.Playback = playback;
        foreach (var member in Participants)
            if (member.Id == userId) member.Playback = playback;
    }

    private async Task LoadUserBannerAsync(Guid userId)
    {
        if (_visualBudget.Suspended || _presentationDisposed || _lifetime.IsCancellationRequested) return;
        var session = SelectedInstance?.Context.Session;
        var user = session?.User(userId);
        if (user?.Banner is null)
        {
            _bannerAttempts.Remove(userId);
            ApplyBanner(userId, null);
            if (_banners.Remove(userId, out var stale)) stale.Playback.Dispose();
            return;
        }
        var animated = user.Banner.Animated && _visualBudget.Animate;
        var url = animated ? user.Banner.DownloadUrl : user.Banner.ThumbnailUrl ?? user.Banner.DownloadUrl;
        if (_banners.TryGetValue(userId, out var existing) && existing.Url == url)
        {
            ApplyBanner(userId, existing.Playback);
            return;
        }
        if (_bannerLoads >= Math.Max(1, _visualBudget.Downloads)
            || _bannerAttempts.TryGetValue(userId, out var attempted) && attempted == url) return;
        _bannerAttempts[userId] = url;
        if (_banners.Values.Sum(item => item.Playback.DecodedBytes) + 360 * 140 * 4 > BannerBudget) return;
        _bannerLoads++;
        var generation = _avatarGeneration;
        _avatarScope ??= CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        var token = _avatarScope.Token;
        var limit = url == user.Banner.DownloadUrl
            ? (int)Math.Min(Math.Max(user.Banner.Size, 1) + 65_536, ProtocolVersion.MaxAvatarBytes) : 768 * 1024;
        try
        {
            var bytes = await session!.DownloadAsync(url, limit, token);
            if (bytes is null || bytes.Length == 0 || token.IsCancellationRequested || generation != _avatarGeneration
                || !ReferenceEquals(session, SelectedInstance?.Context.Session) || _visualBudget.Suspended) return;
            var current = session.User(userId)?.Banner;
            if (current is null || current.Id != user.Banner.Id
                || !_bannerAttempts.TryGetValue(userId, out var requested) || requested != url) return;
            var retained = _banners.Values.Sum(item => item.Playback.DecodedBytes);
            if (retained + 360 * 140 * 4 > BannerBudget) return;
            var playback = AvatarPlayback.Decode(bytes, 360,
                animated && retained + 16 * 360 * 140 * 4 <= BannerBudget, 16);
            ApplyBanner(userId, null);
            if (_banners.Remove(userId, out var previous)) previous.Playback.Dispose();
            if (session.Me.Id != userId)
            {
                foreach (var id in _banners.Keys.Where(id => id != session.Me.Id).ToArray())
                {
                    if (_banners.Remove(id, out var extra)) extra.Playback.Dispose();
                    ApplyBanner(id, null);
                }
            }
            _banners[userId] = (url, playback);
            ApplyBanner(userId, playback);
        }
        catch { /* Leave the color banner; retry on URL or residency change. */ }
        finally
        {
            _bannerLoads--;
        }
    }

    private void ApplyBanner(Guid userId, AvatarPlayback? playback)
    {
        if (SelectedInstance?.Context.Session?.Me.Id == userId)
            Settings.Profile.SetBannerPlayback(playback);
        foreach (var member in Participants)
            if (member.Id == userId) member.BannerPlayback = playback;
    }

    private void ClearPlaybacks()
    {
        _clearingAvatars = true;
        _avatarGeneration++;
        var previous = _avatarScope;
        _avatarScope = null;
        _avatarAttempts.Clear();
        foreach (var row in Messages) row.Playback = null;
        foreach (var member in Participants) member.Playback = null;
        foreach (var member in Participants) member.BannerPlayback = null;
        AccountPlayback = null;
        Changed(nameof(AccountPlayback));
        Changed(nameof(HasAvatar));
        Settings.Profile.SetPlayback(null);
        Settings.Profile.SetBannerPlayback(null);
        foreach (var item in _playbacks.Values) item.Playback.Dispose();
        foreach (var item in _banners.Values) item.Playback.Dispose();
        _playbacks.Clear();
        _banners.Clear();
        _bannerAttempts.Clear();
        previous?.Cancel();
        previous?.Dispose();
        _clearingAvatars = false;
    }

    private void ClearRailAvatars()
    {
        foreach (var instance in Instances) instance.ClearAvatar();
    }
}

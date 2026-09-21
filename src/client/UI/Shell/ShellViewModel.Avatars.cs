using Chat.Protocol;
using Chat.UI.Components;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    private readonly Dictionary<Guid, (Uri Url, AvatarPlayback Playback)> _playbacks = [];
    private readonly Dictionary<Guid, Uri> _avatarAttempts = [];
    private CancellationTokenSource? _avatarScope;
    private int _avatarGeneration;
    private int _avatarLoads;
    private bool _clearingAvatars;
    private long AvatarBudget => _visualBudget.AvatarBytes;

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
        foreach (var id in needed)
        {
            if (_avatarLoads >= _visualBudget.Downloads) break;
            _ = LoadUserAvatarAsync(id);
        }
    }

    private async Task LoadUserAvatarAsync(Guid userId)
    {
        if (_visualBudget.Suspended || _presentationDisposed || _lifetime.IsCancellationRequested) return;
        var session = SelectedInstance?.Context.Session;
        var user = session?.User(userId);
        if (user?.Avatar is null)
        {
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
    }

    private void ClearPlaybacks()
    {
        _clearingAvatars = true;
        _avatarGeneration++;
        var previous = _avatarScope;
        _avatarScope = null;
        _avatarAttempts.Clear();
        foreach (var row in Messages) row.Playback = null;
        AccountPlayback = null;
        Changed(nameof(AccountPlayback));
        Changed(nameof(HasAvatar));
        Settings.Profile.SetPlayback(null);
        foreach (var item in _playbacks.Values) item.Playback.Dispose();
        _playbacks.Clear();
        previous?.Cancel();
        previous?.Dispose();
        _clearingAvatars = false;
    }
}

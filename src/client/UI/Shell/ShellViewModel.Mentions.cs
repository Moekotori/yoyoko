using System.Collections.ObjectModel;
using Chat.Core.Messaging;
using Chat.Localization;
using Chat.UI.Chat;
using Chat.UI.Components;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    public const int MentionCap = 8;
    private bool _mentionOpen;
    private bool _mentionJustInserted;
    private int _mentionCaret;
    private MentionItem? _mentionSelected;
    public ObservableCollection<MentionItem> MentionMatches { get; } = [];
    public ActionCommand PickMention { get; private set; } = null!;
    public Action<int>? PlaceComposerCaret { get; set; }
    public bool MentionOpen
    {
        get => _mentionOpen;
        set
        {
            if (_mentionOpen == value) return;
            _mentionOpen = value;
            if (!value)
            {
                MentionMatches.Clear();
                _mentionSelected = null;
                Changed(nameof(MentionSelected));
            }
            Changed();
        }
    }
    public MentionItem? MentionSelected => _mentionSelected;

    private void InitializeMentions()
    {
        PickMention = new(value =>
        {
            if (value is MentionItem item) TryInsertMention(Draft, _mentionCaret, item);
        });
    }

    public void RefreshMention(string text, int caret)
    {
        if (_mentionJustInserted)
        {
            _mentionJustInserted = false;
            return;
        }
        if (!ShowChat || ChannelForbidden || SelectedChannel is not { CanChat: true })
        {
            MentionOpen = false;
            return;
        }
        if (!MessageMarkup.TryComposerQuery(text, caret, out var query))
        {
            MentionOpen = false;
            return;
        }
        var next = MentionSuggestions(query.Filter);
        if (next.Count == 0)
        {
            MentionOpen = false;
            return;
        }
        _mentionCaret = caret;
        SyncMentions(next);
        MentionOpen = true;
    }

    public void MoveMention(int delta)
    {
        if (MentionMatches.Count == 0) return;
        var index = _mentionSelected is null ? 0 : MentionMatches.IndexOf(_mentionSelected);
        if (index < 0) index = 0;
        index = (index + delta) % MentionMatches.Count;
        if (index < 0) index += MentionMatches.Count;
        SetMentionActive(MentionMatches[index]);
    }

    public bool TryInsertMention(string text, int caret, MentionItem? item)
    {
        var picked = item ?? _mentionSelected;
        if (picked is null || !MessageMarkup.TryComposerQuery(text, caret, out var query))
        {
            MentionOpen = false;
            return false;
        }
        var token = picked.Token;
        if (token.Length == 0 || token[0] != '@') token = "@" + token;
        if (!token.EndsWith(' ')) token += " ";
        _mentionJustInserted = true;
        Draft = text[..query.At] + token + text[query.End..];
        MentionOpen = false;
        PlaceComposerCaret?.Invoke(query.At + token.Length);
        return true;
    }

    public void CloseMention() => MentionOpen = false;

    private List<MentionItem> MentionSuggestions(string filter)
    {
        var next = new List<MentionItem>(MentionCap);
        if (filter.Length == 0
            || MessageMarkup.Everyone.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            next.Add(new MentionItem(_text.Get(TextKey.MentionEveryone)));
        if (filter.Length == 0
            || MessageMarkup.Here.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            next.Add(new MentionItem(_text.Get(TextKey.MentionHere), here: true));
        var seen = new HashSet<Guid>();
        foreach (var person in Participants.Concat(VoicePeople()).Concat(CommunityPeople(filter)))
        {
            if (next.Count >= MentionCap) break;
            if ((person.IsFixture && IsSignedIn) || !seen.Add(person.Id)) continue;
            if (filter.Length > 0
                && !person.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !person.Username.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            next.Add(new MentionItem(person));
        }
        return next;
    }

    public MemberProfile? MemberByMention(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var name = token[0] == '@' ? token[1..] : token;
        if (name.Length == 0 || MessageMarkup.IsReserved(name)) return null;
        foreach (var person in Participants.Concat(VoicePeople()))
        {
            if (person.Username.Equals(name, StringComparison.OrdinalIgnoreCase)
                || person.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return person;
        }
        var session = SelectedInstance?.Context.Session;
        if (session is null) return null;
        foreach (var user in session.KnownUsers)
        {
            if (!user.Username.Equals(name, StringComparison.OrdinalIgnoreCase)
                && !user.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            return ProfileOf(user.Id, user.DisplayName);
        }
        return null;
    }

    private IEnumerable<MemberProfile> CommunityPeople(string filter)
    {
        var session = SelectedInstance?.Context.Session;
        if (session is null) yield break;
        var matches = 0;
        foreach (var user in session.KnownUsers)
        {
            if (matches >= MentionCap) yield break;
            if (filter.Length > 0
                && !user.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !user.Username.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            matches++;
            var existing = Participants.FirstOrDefault(item => item.Id == user.Id);
            if (existing is not null)
            {
                yield return existing;
                continue;
            }
            yield return new MemberProfile(user.Id, user.DisplayName, user.Username, user.Id == session.Me.Id);
        }
    }

    private void SyncMentions(List<MentionItem> next)
    {
        for (var i = MentionMatches.Count - 1; i >= 0; i--)
            if (next.All(item => !SameMention(item, MentionMatches[i]))) MentionMatches.RemoveAt(i);
        for (var i = 0; i < next.Count; i++)
        {
            if (i < MentionMatches.Count && SameMention(MentionMatches[i], next[i])) continue;
            var previous = -1;
            for (var j = 0; j < MentionMatches.Count; j++)
                if (SameMention(MentionMatches[j], next[i])) { previous = j; break; }
            if (previous >= 0) MentionMatches.Move(previous, i);
            else MentionMatches.Insert(i, next[i]);
        }
        var keep = _mentionSelected is not null
            ? MentionMatches.FirstOrDefault(item => SameMention(item, _mentionSelected))
            : null;
        SetMentionActive(keep ?? MentionMatches[0]);
    }

    private static bool SameMention(MentionItem left, MentionItem right) =>
        left.IsEveryone == right.IsEveryone && left.IsHere == right.IsHere && left.Person?.Id == right.Person?.Id;

    private void SetMentionActive(MentionItem? item)
    {
        foreach (var match in MentionMatches) match.IsActive = match == item;
        _mentionSelected = item;
        Changed(nameof(MentionSelected));
    }
}

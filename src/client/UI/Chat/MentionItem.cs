using Chat.UI.Components;

namespace Chat.UI.Chat;

public sealed class MentionItem : ObservableObject
{
    private bool _isActive;

    public MentionItem(string name, bool here = false)
    {
        Name = name;
        IsHere = here;
        IsEveryone = !here;
        Token = "@" + (here
            ? global::Chat.Core.Messaging.MessageMarkup.Here
            : global::Chat.Core.Messaging.MessageMarkup.Everyone);
        Handle = Token;
    }

    public MentionItem(MemberProfile person)
    {
        Person = person;
        Name = person.Name;
        Token = person.MentionToken;
        Handle = person.Handle;
    }

    public MemberProfile? Person { get; }
    public bool IsEveryone { get; }
    public bool IsHere { get; }
    public string Name { get; }
    public string Token { get; }
    public string Handle { get; }
    public bool HasHandle => Handle.Length > 0;
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive == value) return; _isActive = value; Changed(); }
    }
}

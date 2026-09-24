using Chat.UI.Components;

namespace Chat.UI.Auth;

// Presentation state only; authentication remains owned by the selected instance.
public sealed class AuthFormViewModel : ObservableObject
{
    private string _username = "";
    private string _password = "";
    private string _displayName = "";
    private string _status = "";
    public string Username { get => _username; set { _username = value; Changed(); } }
    public string Password { get => _password; set { _password = value; Changed(); } }
    public string DisplayName { get => _displayName; set { _displayName = value; Changed(); } }
    public string Status { get => _status; set { _status = value; Changed(); } }
    private bool _isRegistration;
    private bool _allowRegistration = true;
    private bool _isBusy;

    public AuthFormViewModel(Func<bool, Task> authenticate, Action<Exception> onError)
    {
        ShowSignIn = new(_ => SetMode(false));
        ShowRegistration = new(_ => SetMode(true));
        Submit = new(async () =>
        {
            IsBusy = true;
            try { await authenticate(IsRegistration); }
            finally { IsBusy = false; }
        }, onError);
    }

    public ActionCommand ShowSignIn { get; }
    public ActionCommand ShowRegistration { get; }
    public AsyncCommand Submit { get; }
    public bool IsRegistration => _isRegistration;
    public bool AllowRegistration
    {
        get => _allowRegistration;
        set
        {
            if (_allowRegistration == value) return;
            _allowRegistration = value;
            if (!value) SetMode(false);
            Changed();
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; Changed(); }
    }

    private void SetMode(bool registration)
    {
        if (IsBusy || (registration && !AllowRegistration) || _isRegistration == registration) return;
        _isRegistration = registration;
        Changed(nameof(IsRegistration));
    }
}

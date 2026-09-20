using Chat.UI.Components;

namespace Chat.UI.Auth;

// Presentation state only; authentication remains owned by the selected instance.
public sealed class AuthFormViewModel : ObservableObject
{
    private bool _isRegistration;
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
    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; Changed(); }
    }

    private void SetMode(bool registration)
    {
        if (IsBusy || _isRegistration == registration) return;
        _isRegistration = registration;
        Changed(nameof(IsRegistration));
    }
}

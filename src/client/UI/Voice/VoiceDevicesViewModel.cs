using System.Collections.ObjectModel;
using Chat.Core.Sessions;
using Chat.Core.Voice;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;

namespace Chat.UI.Voice;

public sealed record AudioDeviceChoice(string Id, string Name);

public sealed class VoiceDevicesViewModel : ObservableObject
{
    public const string DefaultId = "";
    private readonly IVoiceMedia _media;
    private readonly IVoiceDevicePreference _preference;
    private readonly I18n _text;
    private readonly Func<InstanceSession?> _session;
    private readonly CancellationToken _lifetime;
    private AudioDeviceChoice? _selectedInput;
    private AudioDeviceChoice? _selectedOutput;
    private string _error = "";
    private bool _updating;
    private bool _loopback;
    private Task? _refresh;
    private bool _applying;
    private bool _applyAgain;
    public VoiceDevicesViewModel(IVoiceMedia media, IVoiceDevicePreference preference, I18n text,
        Func<InstanceSession?> session, CancellationToken lifetime)
    {
        _media = media;
        _preference = preference;
        _text = text;
        _session = session;
        _lifetime = lifetime;
        Inputs.Add(DefaultChoice());
        Outputs.Add(DefaultChoice());
        _selectedInput = string.IsNullOrEmpty(preference.InputDeviceId) ? Inputs[0] : new(preference.InputDeviceId, preference.InputDeviceId);
        _selectedOutput = string.IsNullOrEmpty(preference.OutputDeviceId) ? Outputs[0] : new(preference.OutputDeviceId, preference.OutputDeviceId);
        if (_selectedInput.Id != DefaultId) Inputs.Add(_selectedInput);
        if (_selectedOutput.Id != DefaultId) Outputs.Add(_selectedOutput);
    }
    public ObservableCollection<AudioDeviceChoice> Inputs { get; } = [];
    public ObservableCollection<AudioDeviceChoice> Outputs { get; } = [];
    public AudioDeviceChoice? SelectedInput
    {
        get => _selectedInput;
        set
        {
            if (_updating || _selectedInput?.Id == value?.Id) return;
            _selectedInput = value;
            Changed();
            _ = ApplyAsync();
        }
    }
    public AudioDeviceChoice? SelectedOutput
    {
        get => _selectedOutput;
        set
        {
            if (_updating || _selectedOutput?.Id == value?.Id) return;
            _selectedOutput = value;
            Changed();
            _ = ApplyAsync();
        }
    }
    public string Error { get => _error; private set { _error = value; Changed(); Changed(nameof(HasError)); } }
    public bool HasError => Error.Length > 0;
    public bool LoopbackActive => _loopback;
    public string LoopbackLabel => _text.Get(_loopback ? TextKey.StopDeviceCheck : TextKey.CheckDevices);
    public AudioRoute Route => CurrentRoute();

    public Task RefreshAsync()
    {
        if (_refresh is { IsCompleted: false }) return _refresh;
        return _refresh = RefreshDevicesAsync();
    }

    private async Task RefreshDevicesAsync()
    {
        try
        {
            var list = await _media.ListDevicesAsync(_lifetime);
            Replace(Inputs, list.Inputs);
            Replace(Outputs, list.Outputs);
            Error = "";
        }
        catch (Exception exception)
        {
            Error = _text.Get(TextKey.DevicesUnavailable, exception.Message);
            return;
        }
        Select(_preference.InputDeviceId, _preference.OutputDeviceId);
        _session()?.Voice.ApplyRoute(CurrentRoute());
    }

    public void Relabel()
    {
        _updating = true;
        if (Inputs.Count > 0) Inputs[0] = DefaultChoice();
        if (Outputs.Count > 0) Outputs[0] = DefaultChoice();
        if (_selectedInput?.Id == DefaultId) _selectedInput = Inputs[0];
        if (_selectedOutput?.Id == DefaultId) _selectedOutput = Outputs[0];
        _updating = false;
        Changed(nameof(SelectedInput));
        Changed(nameof(SelectedOutput));
        Changed(nameof(LoopbackLabel));
    }

    public async Task ToggleLoopbackAsync()
    {
        if (_loopback) { await StopLoopbackAsync(); return; }
        try
        {
            var capture = _session()?.Voice.Capture ?? AudioQualities.Profile(AudioQualities.Studio);
            await _media.StartLoopbackAsync(capture, CurrentRoute(), _lifetime);
            _loopback = true;
            Error = "";
        }
        catch (Exception exception)
        {
            _loopback = false;
            Error = _text.Get(TextKey.DevicesUnavailable, exception.Message);
        }
        Changed(nameof(LoopbackActive));
        Changed(nameof(LoopbackLabel));
    }

    public async Task StopLoopbackAsync()
    {
        if (!_loopback) return;
        _loopback = false;
        try { await _media.StopLoopbackAsync(_lifetime); } catch (Exception) { }
        Changed(nameof(LoopbackActive));
        Changed(nameof(LoopbackLabel));
    }

    private async Task ApplyAsync()
    {
        if (_applying) { _applyAgain = true; return; }
        _applying = true;
        try
        {
            do
            {
                _applyAgain = false;
                await ApplyRouteAsync();
            } while (_applyAgain && !_lifetime.IsCancellationRequested);
        }
        finally { _applying = false; }
    }

    private async Task ApplyRouteAsync()
    {
        var route = CurrentRoute();
        _preference.SetDevices(route.InputDeviceId, route.OutputDeviceId);
        var voice = _session()?.Voice;
        try
        {
            if (voice is not null) await voice.SetRouteAsync(route, _lifetime);
            else if (_loopback) await _media.SetDevicesAsync(route, _lifetime);
            Error = "";
        }
        catch (Exception exception)
        {
            Error = _text.Get(TextKey.DevicesUnavailable, exception.Message);
        }
    }

    private AudioRoute CurrentRoute() => new(
        string.IsNullOrEmpty(_selectedInput?.Id) ? null : _selectedInput.Id,
        string.IsNullOrEmpty(_selectedOutput?.Id) ? null : _selectedOutput.Id);

    private void Replace(ObservableCollection<AudioDeviceChoice> target, IReadOnlyList<AudioDevice> devices)
    {
        _updating = true;
        target.Clear();
        target.Add(DefaultChoice());
        foreach (var device in devices)
            if (!string.IsNullOrWhiteSpace(device.Id) && !string.IsNullOrWhiteSpace(device.Name))
                target.Add(new(device.Id, device.Name));
        _updating = false;
    }

    private void Select(string? inputId, string? outputId)
    {
        _updating = true;
        _selectedInput = Inputs.FirstOrDefault(item => item.Id == (inputId ?? DefaultId)) ?? Inputs[0];
        _selectedOutput = Outputs.FirstOrDefault(item => item.Id == (outputId ?? DefaultId)) ?? Outputs[0];
        _updating = false;
        Changed(nameof(SelectedInput));
        Changed(nameof(SelectedOutput));
    }

    private AudioDeviceChoice DefaultChoice() => new(DefaultId, _text.Get(TextKey.DefaultDevice));
}

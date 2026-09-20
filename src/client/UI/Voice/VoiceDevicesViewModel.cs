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
        _selectedInput = Inputs[0];
        _selectedOutput = Outputs[0];
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
    public AudioRoute Route => CurrentRoute();

    public async Task RefreshAsync()
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
            Replace(Inputs, []);
            Replace(Outputs, []);
            Error = _text.Get(TextKey.DevicesUnavailable, exception.Message);
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
    }

    private async Task ApplyAsync()
    {
        var route = CurrentRoute();
        _preference.SetDevices(route.InputDeviceId, route.OutputDeviceId);
        var voice = _session()?.Voice;
        try
        {
            if (voice is not null) await voice.SetRouteAsync(route, _lifetime);
            else await _media.SetDevicesAsync(route, _lifetime);
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

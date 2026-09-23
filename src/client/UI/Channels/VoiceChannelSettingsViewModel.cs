using System.Collections.ObjectModel;
using System.Text;
using Chat.Core.Sessions;
using Chat.Core.Voice;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;
using Chat.UI.Voice;

namespace Chat.UI.Channels;

public sealed class VoiceChannelSettingsViewModel : ObservableObject
{
    private string _name;
    private AudioQualityChoice? _quality;
    private string _error = "";
    private bool _busy;

    public VoiceChannelSettingsViewModel(InstanceSession session, ChannelItem channel, bool canManage,
        I18n text, Action saved, Action closed, CancellationToken lifetime)
    {
        _name = channel.Name;
        CanManage = canManage;
        foreach (var id in AudioQualities.All)
        {
            var (title, detail) = id switch
            {
                AudioQualities.Standard => (TextKey.QualityStandard, TextKey.QualityStandardDetail),
                AudioQualities.High => (TextKey.QualityHigh, TextKey.QualityHighDetail),
                AudioQualities.VeryHigh => (TextKey.QualityVeryHigh, TextKey.QualityVeryHighDetail),
                _ => (TextKey.QualityStudio, TextKey.QualityStudioDetail)
            };
            Qualities.Add(new(id, text.Get(title), text.Get(detail)));
        }
        _quality = Qualities.FirstOrDefault(item => item.Id == channel.AudioQuality) ?? Qualities.LastOrDefault();
        Close = new(_ => { if (!IsBusy) closed(); });
        Save = new(async () =>
        {
            if (!CanManage || IsBusy) return;
            Error = "";
            if (string.IsNullOrWhiteSpace(Name) || Encoding.UTF8.GetByteCount(Name.Trim()) > 100)
            { Error = text.Get(TextKey.ChannelNameInvalid); return; }
            if (Quality is null) return;
            if (Name.Trim() == channel.Name && Quality.Id == channel.AudioQuality) { closed(); return; }
            IsBusy = true;
            try
            {
                await session.UpdateVoiceChannelAsync(channel.Id, Name, Quality.Id, lifetime);
                saved();
            }
            finally { IsBusy = false; }
        }, exception => Error = text.Error(exception));
    }

    public string Name { get => _name; set { _name = value; Changed(); } }
    public AudioQualityChoice? Quality { get => _quality; set { _quality = value; Changed(); } }
    public ObservableCollection<AudioQualityChoice> Qualities { get; } = [];
    public bool CanManage { get; }
    public string Error { get => _error; private set { _error = value; Changed(); Changed(nameof(HasError)); } }
    public bool HasError => Error.Length > 0;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); } }
    public AsyncCommand Save { get; }
    public ActionCommand Close { get; }
}

using System.Collections.ObjectModel;
using Chat.Core.Instances;
using Chat.UI.Components;
using Chat.UI.Instances;

namespace Chat.UI.Shell;

public sealed class ShellViewModel : ObservableObject
{
    private readonly InstanceManager _instances;
    private readonly CancellationToken _lifetime;
    private string _address = "";
    private string _status = "";
    private InstanceItem? _selected;
    public ShellViewModel(InstanceManager instances, string productName, CancellationToken lifetime)
    {
        _instances = instances;
        ProductName = productName;
        _lifetime = lifetime;
        AddInstance = new(AddAsync, exception => Status = exception is OperationCanceledException
            ? "连接已取消。" : exception.Message);
    }
    public string ProductName { get; }
    public ObservableCollection<InstanceItem> Instances { get; } = [];
    public AsyncCommand AddInstance { get; }
    public string Address { get => _address; set { _address = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public InstanceItem? SelectedInstance
    {
        get => _selected;
        set { _selected = value; Changed(); Changed(nameof(InstanceName)); Changed(nameof(InstanceHost)); }
    }
    public string InstanceName => SelectedInstance?.Name ?? "工作空间";
    public string InstanceHost => SelectedInstance?.Host ?? "尚未连接实例";
    public string[] Channels { get; } = [];

    public async Task InitializeAsync(Func<CancellationToken, Task> initializeCache)
    {
        try
        {
            await initializeCache(_lifetime);
            await _instances.LoadCachedAsync(_lifetime);
            foreach (var item in _instances.Contexts) Instances.Add(new(item));
            SelectedInstance = Instances.FirstOrDefault();
        }
        catch (Exception exception) { Status = "本地缓存加载失败：" + exception.Message; }
    }
    private async Task AddAsync()
    {
        Status = "正在发现实例…";
        var instance = await _instances.AddAsync(Address, _lifetime);
        var item = Instances.FirstOrDefault(item => item.Context.Descriptor.Id == instance.Descriptor.Id);
        if (item is null) { item = new(instance); Instances.Add(item); }
        SelectedInstance = item;
        Status = "已保存实例。账号登录将在下一阶段开放。";
        Address = "";
    }
}

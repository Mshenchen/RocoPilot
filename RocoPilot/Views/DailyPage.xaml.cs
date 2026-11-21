using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using RocoPilot.ViewModels;

namespace RocoPilot.Views;

public sealed partial class DailyPage : Page
{
    private DailyTaskItemViewModel? _moveSource;

    public DailyViewModel ViewModel { get; } = App.GetService<DailyViewModel>();

    public TasksViewModel Challenges { get; } = App.GetService<TasksViewModel>();

    public DailyPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => ViewModel.NoticeRequested -= ShowNotice;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.NoticeRequested += ShowNotice;
        await Challenges.LoadAsync();
        await ViewModel.LoadAsync();
    }

    /// <summary>提权时的替代交互：第一次点击选中任务，第二次点击目标任务把它移到该位置。</summary>
    private void TaskList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (!ViewModel.CanClickReorder || e.ClickedItem is not DailyTaskItemViewModel clicked)
        {
            return;
        }

        if (_moveSource is null || !ViewModel.Items.Contains(_moveSource))
        {
            SelectMoveSource(clicked);
            return;
        }

        if (ReferenceEquals(_moveSource, clicked))
        {
            ClearMoveSource();
            return;
        }

        var source = _moveSource;
        ClearMoveSource();
        ViewModel.TryMoveItem(source, clicked);
    }

    private void SelectMoveSource(DailyTaskItemViewModel item)
    {
        ClearMoveSource();
        _moveSource = item;
        item.IsMoveSource = true;
    }

    private void ClearMoveSource()
    {
        if (_moveSource is { } source)
        {
            source.IsMoveSource = false;
            _moveSource = null;
        }
    }

    private void ShowNotice(object? sender, string message)
    {
        DailyNotice.IsOpen = false;
        DailyNotice.Message = message;
        DailyNotice.IsOpen = true;
    }
}

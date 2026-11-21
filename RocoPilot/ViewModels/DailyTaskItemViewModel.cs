using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml;

using RocoPilot.Helpers;
using RocoPilot.Models.Runtime;

namespace RocoPilot.ViewModels;

public partial class DailyTaskItemViewModel(DailyTaskKind kind) : ObservableObject
{
    private bool _isMoveSource;

    public DailyTaskKind Kind { get; } = kind;

    /// <summary>提权时拖拽不可用，点击排序中待移动的任务用强调色边框标出。</summary>
    public bool IsMoveSource
    {
        get => _isMoveSource;
        set
        {
            if (SetProperty(ref _isMoveSource, value))
            {
                OnPropertyChanged(nameof(MoveSourceVisibility));
            }
        }
    }

    public Visibility MoveSourceVisibility => _isMoveSource ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>排序手柄的悬浮提示：按运行权限给出对应操作方式。</summary>
    public string ReorderToolTip => AppElevation.IsElevated
        ? "管理员权限下拖拽不可用：点击一个任务选中，再点击另一个任务即可把它移动到该位置。"
        : "拖动任务可调整顺序。";

    public IndependentTaskKind? TaskKind => Kind switch
    {
        DailyTaskKind.BattlePassRewards => IndependentTaskKind.BattlePassRewards,
        DailyTaskKind.Mail => IndependentTaskKind.Mail,
        DailyTaskKind.Sell => IndependentTaskKind.Sell,
        DailyTaskKind.Gardening => IndependentTaskKind.Gardening,
        DailyTaskKind.EveningRest => IndependentTaskKind.EveningRest,
        DailyTaskKind.StaminaCrafting => IndependentTaskKind.StaminaCrafting,
        DailyTaskKind.LegendaryChallenge => IndependentTaskKind.LegendaryChallenge,
        DailyTaskKind.FlowerSeedChallenge => IndependentTaskKind.FlowerSeedChallenge,
        _ => null
    };

    public bool IsAvailable => TaskKind is not null;

    public string Title => Kind switch
    {
        DailyTaskKind.StaminaCrafting => "体力合成",
        DailyTaskKind.Mail => "邮件领取",
        DailyTaskKind.LegendaryChallenge => "传说精灵挑战",
        DailyTaskKind.FlowerSeedChallenge => "花种挑战",
        DailyTaskKind.Gardening => "自动种菜",
        DailyTaskKind.BattlePassRewards => "通行证奖励",
        DailyTaskKind.Inspiration => "自动灵感",
        DailyTaskKind.Sell => "自动出售",
        DailyTaskKind.EveningRest => "晚风小憩",
        _ => string.Empty
    };

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "待接入";

    [ObservableProperty]
    public partial bool CanToggle { get; set; }
}

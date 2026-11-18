using RocoPilot.Configuration;
using RocoPilot.Models.ImageMatching;
using RocoPilot.Models.Recognition;
using RocoPilot.Services.RuntimeTasks;

namespace RocoPilot.Services;

/// <summary>
/// 敌方精灵血量识别：模板匹配战斗界面右上角的敌方血条。
/// 第一回合血条即呈现 10% 解除形态（左侧一小段红色填充 + "10 %" 文本）时，
/// 判定奇遇效果已解除，由自动战斗按解除操作设置执行对应动作。
/// </summary>
internal static class EncounterEnemyHealthRecognition
{
    public static IReadOnlyList<string> RegionIds { get; } = [RecognitionRegionIds.BattleEnemyHealth];

    /// <summary>奇遇解除形态的血条模板（左侧约 10% 红色填充 + "10 %" 文本）。</summary>
    public const string RelievedTemplateName = "battle-enemy-health-relieved.png";

    public static readonly ImageMatchOptions RelievedMatchOptions = new()
    {
        // 模板取自清晰截图，实测命中 0.95 左右，阈值取 0.90。
        MinimumScore = 0.90,
        AlphaThreshold = 16,
        SearchStep = 1
    };

    public static bool IsAvailable(RecognitionRegionConfig config) => config.Regions.FirstOrDefault(region =>
        RuntimeFrameRecognizer.IsRegionMatch(region, RegionIds)) is { Width: > 0, Height: > 0 };
}

using Microsoft.Extensions.Logging;

using RocoPilot.Helpers;
using RocoPilot.Models.Capture;
using RocoPilot.Models.Runtime;
using RocoPilot.Services.RuntimeTasks;
using static RocoPilot.Services.RuntimeTasks.RuntimeFrameRecognizer;

namespace RocoPilot.Services;

public sealed partial class RuntimeTaskService
{
    /// <summary>
    /// 第一回合模板匹配敌方血条：战斗刚开始（尚未完成首次技能释放）血条就呈现
    /// 10% 解除形态时，判定奇遇效果已解除，按解除操作设置执行对应动作并计入奇遇统计。
    /// </summary>
    private async Task TryObserveEnemyHealthForReliefAsync(
        RuntimeTaskState state,
        CapturedFrame frame,
        long battleId,
        CancellationToken cancellationToken)
    {
        if (!EncounterEnemyHealthRecognition.IsAvailable(state.RecognitionRegionConfig)
            || _battle.RoundIndex != 0
            || _battle.IsEncounterRelieved
            || _battle.IsSuspendedForShiny
            || !AutoBattleSettingsRules.RequiresReliefDetection(_autoBattleSettings.EncounterRelievedAction))
        {
            return;
        }

        var templatePath = GetResolutionTemplatePath(
            state.RecognitionRegionConfig,
            EncounterEnemyHealthRecognition.RelievedTemplateName);
        if (!_frameRecognizer.TemplateExists(templatePath))
        {
            return;
        }

        var match = await _frameRecognizer.MatchRuntimeTemplateResultAsync(
            state,
            frame,
            EncounterEnemyHealthRecognition.RegionIds,
            EncounterEnemyHealthRecognition.RelievedTemplateName,
            EncounterEnemyHealthRecognition.RelievedMatchOptions,
            cancellationToken);
        if (!match.IsMatch
            || battleId != _battle.BattleId
            || _battle.RoundIndex != 0)
        {
            return;
        }

        _logger.LogInformation(
            "自动战斗：第一回合敌方血条呈解除形态（相似度 {Score:F3}），判定奇遇效果已解除。",
            match.Score);
        // 血量解除与捕捉按钮解除同源，置位后奇遇统计同样记录本场精灵。
        _encounterCaptureButtonStateTracker.MarkRelieved();
        ApplyAutoBattleEncounterRelievedDetection("敌方血量");
    }
}

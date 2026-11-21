using System.Diagnostics;
using RocoPilot.Contracts.Services;
using RocoPilot.Contracts.Services.Capture;
using RocoPilot.Contracts.Services.ImageMatching;
using RocoPilot.Models.ImageMatching;
using RocoPilot.Models.Runtime;

namespace RocoPilot.Services.IndependentTasks;

public sealed class StaminaCraftingRunner(IScreenCaptureService capture, IImageMatchingService images,
    IKeyboardInputService keyboard, IMouseInputService mouse)
{
    private const int FramePollMs = 350;
    private const int ActionPauseMs = 900;
    private const int MaxDialogueClicks = 8;
    private const int MaxSliderDrags = 2;

    private static readonly TimeSpan ResultPause = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ConvertPanelWait = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MakeVerify = TimeSpan.FromSeconds(6);

    // 以下均为参考分辨率 2048x1152 下的界面坐标。
    // 左侧类别栏第 4 个图标即“转化炼金”，列表第一格固定是凝练印记（紫色星星）。
    private const double ConvertTabX = 173;
    private const double ConvertTabY = 516;
    // 大世界对话气泡内部：气泡逐字出现时点这里可立即补全并推进。
    private const double DialogueClickX = 1061;
    private const double DialogueClickY = 920;
    // 制造次数滑块轨道右端稍外侧：拖到这里由游戏夹到最大次数，但仍避开“+”按钮。
    private const double SliderMaxX = 1555;
    // 炼金界面右上角关闭按钮（X）。
    private const double ClosePanelX = 1900;
    private const double ClosePanelY = 61;

    // 炼金详情面板：图标区（紫色星星）+ 名称区（“凝练印记”），两者同时命中才算选中凝练印记。
    private const int StarseedLeft = 250;
    private const int StarseedTop = 860;
    private const int StarseedRight = 445;
    private const int StarseedBottom = 1040;
    private const double StarseedThreshold = .80;
    private const int MarkNameLeft = 430;
    private const int MarkNameTop = 895;
    private const int MarkNameRight = 590;
    private const int MarkNameBottom = 965;
    private const double MarkNameThreshold = .85;

    public async Task<string> RunAsync(RuntimeTaskState state, Action<IndependentTaskProgress> progress, CancellationToken token)
    {
        var window = state.TargetWindow;
        var input = state.Options.AutoBattleSettings;
        keyboard.EnsureReady(input.KeyboardInputMethod);
        var step = Step.OpenMap;
        var outcome = "成功：已制造凝练印记";
        var timer = Stopwatch.StartNew();
        var nextResultEscape = ResultPause;
        DateTimeOffset? lastResultFrame = null;
        var freshAfter = DateTimeOffset.UtcNow;
        var dialogueClicks = 0;
        var convertClicks = 0;
        var sliderDrags = 0;
        progress(new("体力合成", "打开地图"));

        while (true)
        {
            await Task.Delay(FramePollMs, token);
            if (!keyboard.IsWindowAvailable(window.Hwnd)) throw new InvalidOperationException("游戏窗口已关闭。");
            if (!keyboard.IsWindowForeground(window.Hwnd))
            {
                if (timer.IsRunning) progress(new("体力合成", "已暂停，等待切回游戏"));
                timer.Stop();
                lastResultFrame = null;
                freshAfter = DateTimeOffset.UtcNow;
                continue;
            }
            if (!timer.IsRunning)
            {
                timer.Start();
                progress(new("体力合成", Describe(step)));
            }
            if (timer.Elapsed > TimeSpan.FromSeconds(45))
                throw new TimeoutException($"体力合成等待超时：{Describe(step)}。");

            using var frame = await Task.Run(() => capture.Capture(window, state.Options.CaptureMethod), token);
            // 静止的结算画面可能只返回上一帧；允许在同一帧上按间隔重试 ESC。
            if (frame is null || (frame.CapturedAt <= freshAfter &&
                !(step == Step.ExitRecipe && frame.CapturedAt == lastResultFrame && timer.Elapsed >= nextResultEscape))) continue;
            var screen = new DailyTaskScreen(frame, window, images);
            Task<ImageMatchResult?> Find(string name, int left, int top, int right, int bottom, double threshold = .94) =>
                screen.Find($"stamina-crafting/{name}", left, top, right, bottom, token, threshold);
            // 制造按钮在可制造与置灰两种状态下外观略有差异，两种模板都视为“按钮在位”。
            async Task<ImageMatchResult?> MakeButton() =>
                await Find("make", 1640, 1010, 1960, 1140)
                ?? await Find("make-disabled", 1640, 1010, 1960, 1140);
            Task<ImageMatchResult?> SliderHandle() => Find("handle", 1150, 950, 1450, 1090);
            Task<ImageMatchResult?> Choices() => Find("choices", 1420, 620, 1950, 735, .90);
            Task<ImageMatchResult?> Skip() => screen.Find("flower-seed/animation-skip", 80, 10, 240, 146, token);
            // 炼金造物界面就绪：制造按钮或制造次数滑块出现。
            async Task<bool> AlchemyPanel() => await MakeButton() is not null || await SliderHandle() is not null;
            // 详情面板里的凝练印记：紫色星星图标 + “凝练印记”名称双重确认，
            // 避免同色的其它炼金物品（如净化灵药）只凭图标被误判。
            Task<ImageMatchResult?> Starseed() => Find(
                "starseed", StarseedLeft, StarseedTop, StarseedRight, StarseedBottom, StarseedThreshold);
            Task<ImageMatchResult?> MarkName() => Find(
                "mark-name", MarkNameLeft, MarkNameTop, MarkNameRight, MarkNameBottom, MarkNameThreshold);
            async Task<bool> IsConcentratedMark() =>
                await Starseed() is not null && await MarkName() is not null;
            async Task AfterInput()
            {
                freshAfter = DateTimeOffset.UtcNow;
                await Task.Delay(ActionPauseMs, token);
            }

            async Task ClickClient(int x, int y)
            {
                await mouse.ClickAsync(window.Hwnd, x, y, input.KeyboardInputMethod, token);
                await AfterInput();
            }

            // 匹配结果已经是帧内像素坐标。
            async Task Click(ImageMatchResult target) =>
                await ClickClient(screen.ClientX(target.X + target.Width / 2d),
                    screen.ClientY(target.Y + target.Height / 2d));

            // 手写常量是 2048x1152 参考坐标，必须换算成客户区像素。
            async Task ClickReference(double referenceX, double referenceY)
            {
                var (x, y) = screen.ReferenceToClient(referenceX, referenceY);
                await ClickClient(x, y);
            }

            async Task Key(string key, int? hold = null)
            {
                if (!keyboard.IsWindowForeground(window.Hwnd))
                    throw new InvalidOperationException("游戏窗口已离开前台。");
                await keyboard.SendSequenceAsync(window.Hwnd, key,
                    new() { Method = input.KeyboardInputMethod, HoldDurationMs = hold ?? input.KeyboardHoldDurationMs }, token);
                await AfterInput();
            }

            var previous = step;
            switch (step)
            {
                case Step.OpenMap:
                    if (await screen.Map(token) is not null) step = Step.SelectMap;
                    else if (await screen.World(token) is not null)
                    {
                        await Key("M");
                        step = Step.SelectMap;
                    }
                    break;
                case Step.SelectMap:
                    var (selected, target) = await screen.SelectGarden(token);
                    if (target != null) await Click(target);
                    if (selected) step = Step.Cauldron;
                    break;
                case Step.Cauldron:
                    if (await screen.Garden(token) is not null &&
                        await Find("cauldron", 350, 210, 1530, 960) is { } marker)
                    {
                        await Click(marker);
                        step = Step.Teleport;
                    }
                    break;
                case Step.Teleport:
                    if (await Find("teleport-title", 1280, 0, 1790, 118) is not null &&
                        await screen.Teleport(token) is { } teleport)
                    {
                        await Click(teleport);
                        step = Step.Approach;
                    }
                    break;
                case Step.Approach:
                    if (await Find("interact", 1100, 420, 1610, 800) is not null)
                    {
                        await Key("F");
                        step = Step.Dialogue;
                    }
                    else if (await screen.World(token) is not null) await Key("W", 300);
                    break;
                case Step.Dialogue:
                    if (await Choices() is not null)
                    {
                        await Key("1");
                        step = Step.OpenAlchemy;
                    }
                    else if (await Skip() is not null || await Find("playing", 1700, 0, 1980, 102) is not null)
                    {
                        await Key("Esc");
                        if (dialogueClicks < MaxDialogueClicks)
                        {
                            dialogueClicks++;
                            await ClickReference(DialogueClickX, DialogueClickY);
                        }
                    }
                    else if (dialogueClicks > 0 && dialogueClicks < MaxDialogueClicks)
                    {
                        // ESC 只跳过过场动画，对话气泡仍在逐字出现：点一下气泡让文本立刻补全并推进到下一句。
                        dialogueClicks++;
                        await ClickReference(DialogueClickX, DialogueClickY);
                    }
                    break;
                case Step.OpenAlchemy:
                    if (await AlchemyPanel())
                    {
                        convertClicks = 1;
                        await ClickReference(ConvertTabX, ConvertTabY);
                        step = Step.PickConvert;
                    }
                    break;
                case Step.PickConvert:
                    if (await IsConcentratedMark())
                    {
                        step = Step.SetAmount;
                    }
                    else if (convertClicks < 2)
                    {
                        // 类别切换偶尔吞掉第一次点击，补一次。
                        convertClicks++;
                        await ClickReference(ConvertTabX, ConvertTabY);
                    }
                    else if (timer.Elapsed > ConvertPanelWait)
                    {
                        outcome = "跳过：炼金界面未选中凝练印记";
                        await Key("Esc");
                        step = Step.ExitDialogue;
                    }
                    break;
                case Step.SetAmount:
                    if (await SliderHandle() is { } handle && sliderDrags < MaxSliderDrags)
                    {
                        var handleClientX = screen.ClientX(handle.X + handle.Width / 2d);
                        var handleClientY = screen.ClientY(handle.Y + handle.Height / 2d);
                        var (maxClientX, _) = screen.ReferenceToClient(SliderMaxX, 0);
                        if (handleClientX >= maxClientX - screen.Scale * 40)
                        {
                            step = Step.Make;
                            break;
                        }

                        sliderDrags++;
                        await mouse.DragAsync(window.Hwnd, handleClientX, handleClientY,
                            maxClientX, handleClientY, input.KeyboardInputMethod, token);
                        await AfterInput();
                        if (sliderDrags >= MaxSliderDrags) step = Step.Make;
                        break;
                    }
                    step = Step.Make;
                    break;
                case Step.Make:
                    if (await MakeButton() is { } make)
                    {
                        await Click(make);
                        step = Step.ConfirmMake;
                    }
                    break;
                case Step.ConfirmMake:
                    // 制造会消耗材料与金币。复查同一按钮，确认消失后再开始计时。
                    if (await MakeButton() is null) step = Step.ExitRecipe;
                    else if (timer.Elapsed > MakeVerify)
                    {
                        outcome = "跳过：材料不足，制造未生效";
                        await Key("Esc");
                        step = Step.ExitDialogue;
                    }
                    break;
                case Step.ExitRecipe:
                    if (timer.Elapsed < nextResultEscape) break;
                    lastResultFrame = frame.CapturedAt;
                    if (await AlchemyPanel())
                    {
                        // 结算结束回到炼金界面：点右上角 X 关闭界面。
                        await ClickReference(ClosePanelX, ClosePanelY);
                        step = Step.ExitDialogue;
                    }
                    else
                    {
                        // 仍在结算动画上：先关掉结算。
                        await Key("Esc");
                        nextResultEscape = timer.Elapsed + ResultPause;
                    }
                    break;
                case Step.ExitDialogue:
                    if (await Choices() is not null)
                    {
                        await Key("Esc");
                        step = Step.ReturnWorld;
                    }
                    else if (await AlchemyPanel())
                    {
                        // 界面还在（含材料不足直接退出）：补一次右上角 X。
                        await ClickReference(ClosePanelX, ClosePanelY);
                    }
                    else if (await Skip() is not null || await Find("playing", 1700, 0, 1980, 102) is not null)
                    {
                        await Key("Esc");
                    }
                    break;
                case Step.ReturnWorld:
                    if (await screen.World(token) is not null) return outcome;
                    if (await Choices() is not null) await Key("Esc");
                    break;
            }

            if (previous != step)
            {
                timer.Restart();
                progress(new("体力合成", Describe(step)));
            }
        }
    }

    private enum Step
    {
        OpenMap, SelectMap, Cauldron, Teleport, Approach, Dialogue,
        OpenAlchemy, PickConvert, SetAmount, Make, ConfirmMake, ExitRecipe, ExitDialogue, ReturnWorld
    }

    private static string Describe(Step step) => step switch
    {
        Step.OpenMap or Step.SelectMap => "选择家园种植园地图",
        Step.Cauldron or Step.Teleport => "传送到炼金釜",
        Step.Approach => "接近炼金釜",
        Step.Dialogue => "进入炼金造物",
        Step.OpenAlchemy => "打开炼金界面",
        Step.PickConvert => "切换到转化炼金并确认凝练印记",
        Step.SetAmount => "将制造次数拉满",
        Step.Make => "点击制造",
        Step.ConfirmMake => "确认制造结果",
        Step.ExitRecipe => "等待结算并返回炼金界面",
        _ => "退出炼金并返回大世界"
    };
}

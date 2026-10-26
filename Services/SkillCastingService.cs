using WOWCV.Models;

namespace WOWCV.Services;

public sealed record CastingStatus(bool Enabled, string? ActivePress, string? LastSkill,
    string Reason, long CompletedCount = 0, long FrameId = 0);

/// <summary>One serialized casting worker. All native key ownership belongs to this
/// worker.</summary>
public sealed class SkillCastingService : IDisposable
{
    private readonly object gate = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly ISendInput sender;
    private readonly TimeProvider clock;
    private readonly SkillDecisionEngine engine;
    private readonly HashSet<Keys> physicalKeys = [];
    private readonly Thread worker;
    private SkillCastingPlan? plan;
    private SkillTriggerState? triggers;
    private CastingConfiguration options = new();
    private SkillObservationSnapshot snapshot = SkillObservationSnapshot.Empty;
    private long contextVersion, generation, activationAfter, lastSentAt = -1;
    private long completedCount;
    private bool contextAllowed, enabled, started, disposed;
    private CastingStatus status = new(false, null, null, "技能施放未啟用");
    public CastingStatus Status => Volatile.Read(ref status);

    /// <summary>
    /// 建立施放協調器，注入送鍵與時鐘服務以支援隔離驗證。
    /// </summary>
    public SkillCastingService(ISendInput sender, TimeProvider? clock = null)
    {
        this.sender = sender;
        this.clock = clock ?? TimeProvider.System;
        engine = new(this.clock);
        worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "WOWCV Casting"
        };
    }

    /// <summary>
    /// 啟動等待觸發、新影格與冷卻條件的背景施放工作。
    /// </summary>
    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started)
                return;
            started = true;
            worker.Start();
        }
    }

    /// <summary>
    /// 更新辨識版本與來源可用狀態，讓舊施放工作失效。
    /// </summary>
    public void ConfigureContext(long version, bool allowed)
    {
        lock (gate)
        {
            if (disposed)
                return;
            StopLocked("設定已更新，技能施放未啟用");
            contextVersion = version;
            contextAllowed = allowed;
            snapshot = SkillObservationSnapshot.Empty;
            wake.Set();
        }
    }

    /// <summary>
    /// 啟用已編譯的施放計畫，保留本次工作階段的共用冷卻。
    /// </summary>
    public void Enable(SkillCastingPlan value, CastingConfiguration configuration, long version)
    {
        configuration.Validate();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!started || !contextAllowed || contextVersion != version)
                throw new InvalidOperationException("請使用螢幕來源、啟用五點驗證並開始偵測，再啟用技能施放。");
            plan = value;
            options = configuration;
            triggers = new(configuration.Mode, value.Flows.Select(flow => flow.Trigger));
            activationAfter = clock.GetTimestamp();
            generation++;
            enabled = true;
            SetStatus("等待觸發鍵；啟用前已按住的鍵須先放開再按");
            wake.Set();
        }
    }

    /// <summary>
    /// 停用施放並取消待執行工作，保留既有冷卻紀錄。
    /// </summary>
    public void Disable(string reason = "技能施放已停用")
    {
        lock (gate)
        {
            if (disposed)
                return;
            StopLocked(reason);
            wake.Set();
        }
    }

    /// <summary>
    /// 在持有同步鎖時停止作用中流程並使待送鍵工作失效。
    /// </summary>
    private void StopLocked(string reason)
    {
        enabled = false;
        triggers = null;
        generation++;
        SetStatus(reason);
    }

    /// <summary>
    /// 發布施放器目前狀態與停止原因，供介面讀取。
    /// </summary>
    private void SetStatus(string reason, string? lastSkill = null) => Volatile.Write(
        ref status,
        new(
            enabled,
            triggers?.Active is { } key
        ? plan?.Flows.FirstOrDefault(flow => flow.Trigger == key)?.Label : null,
            lastSkill ?? status.LastSkill,
            reason,
            completedCount,
            snapshot.FrameId
        )
    );

    /// <summary>
    /// 接收實體鍵盤事件並更新目前觸發流程。
    /// </summary>
    public void KeyboardInput(KeyboardInputEventArgs input)
    {
        if (input.ExtraInformation == WindowsSendInput.InputMarker)
            return;
        lock (gate)
        {
            if (disposed)
                return;
            bool changed = input.IsDown ? physicalKeys.Add(input.Key)
                : physicalKeys.Remove(input.Key);
            if (!changed || !enabled)
                return;
            var previous = triggers!.Active;
            triggers.Update(input.Key, input.IsDown);
            if (previous != triggers.Active)
            {
                activationAfter = clock.GetTimestamp();
                generation++;
                SetStatus(triggers.Active is null ? "等待觸發鍵" : "等待目前流程的新畫面");
            }

            wake.Set();
        }
    }

    /// <summary>
    /// 發布完整技能觀察快照並喚醒等待新影格的施放工作。
    /// </summary>
    public void PublishSnapshot(SkillObservationSnapshot value)
    {
        lock (gate)
        {
            if (disposed || value.SettingsVersion != contextVersion)
                return;
            // Empty/error notifications invalidate evidence just as completed frames replace it.
            snapshot = value;
            wake.Set();
        }
    }

    /// <summary>
    /// 計算指定等待期限尚餘的毫秒數。
    /// </summary>
    private int RemainingMs(long start, int duration, long now)
    {
        double remaining = duration - clock.GetElapsedTime(start, now).TotalMilliseconds;
        return remaining <= 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Ceiling(remaining));
    }

    /// <summary>
    /// 協調觸發、冷卻與新影格等待，每輪最多完整送出一個技能。
    /// </summary>
    private void Run()
    {
        while (true)
        {
            CastingRule? rule = null;
            SkillObservationSnapshot? evidence = null;
            long ticket = 0;
            int wait = Timeout.Infinite;
            lock (gate)
            {
                if (disposed)
                    return;
                if (enabled && triggers?.Active is { } active)
                {
                    long now = clock.GetTimestamp();
                    int interval = lastSentAt < 0 ? 0 : RemainingMs(lastSentAt,
                        options.MinimumIntervalMs, now);
                    if (interval > 0)
                    {
                        wait = interval;
                        SetStatus($"等待共用施放間隔（{interval} ms）");
                    }
                    else if (snapshot.FrameId == 0 || snapshot.SettingsVersion != contextVersion
                        || snapshot.CaptureStartedTicks <= Math.Max(activationAfter, lastSentAt))
                        SetStatus("等待送鍵／觸發後的新畫面完整驗證");
                    else
                    {
                        var flow = plan!.Flows.Single(item => item.Trigger == active);
                        rule = engine.Select(flow, snapshot, now, out var reason);
                        if (rule is not null && physicalKeys.Contains(rule.Key.Main))
                        {
                            rule = null;
                            reason = "等待實體施放按鍵放開";
                        }

                        ticket = generation;
                        SetStatus(reason);
                        evidence = snapshot;
                    }
                }
            }

            if (rule is not null)
                Send(rule, ticket, evidence!);
            else
                wake.WaitOne(wait);
        }
    }

    /// <summary>
    /// 依計畫序列化按下與放開事件，取消或失敗時釋放本次擁有的按鍵。
    /// </summary>
    private void Send(CastingRule rule, long ticket, SkillObservationSnapshot evidence)
    {
        var owned = new List<Keys>();
        Exception? failure = null;
        bool allDown = false;
        try
        {
            lock (gate)
            {
                if (disposed || !enabled || generation != ticket || !ReferenceEquals(snapshot,
                    evidence))
                    return;
                foreach (var key in rule.Key.KeysInOrder)
                {
                    if (CastingKey.IsModifier(key) && ModifierHeld(key))
                        continue;
                    // Include the attempted key so a partially successful native call is still
                    // released.
                    owned.Add(key);
                    sender.KeyDown(key);
                }

                allDown = true;
            }

            long downAt = clock.GetTimestamp();
            while (true)
            {
                int remaining;
                lock (gate)
                {
                    if (disposed || !enabled || generation != ticket)
                        break;
                    remaining = RemainingMs(downAt, options.KeyHoldMs, clock.GetTimestamp());
                }

                if (remaining == 0)
                    break;
                wake.WaitOne(remaining);
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            foreach (var key in owned.AsEnumerable().Reverse())
            {
                try
                {
                    sender.KeyUp(key);
                }
                catch (Exception ex)
                {
                    failure ??= ex;
                }
            }

            lock (gate)
            {
                if (allDown && failure is null)
                {
                    lastSentAt = clock.GetTimestamp();
                    engine.RecordSent(rule, lastSentAt);
                    completedCount++;
                    if (enabled)
                        SetStatus("按鍵已送出，等待共用間隔與新畫面", rule.SkillName);
                    else
                        SetStatus(status.Reason, rule.SkillName);
                }

                if (failure is not null)
                    StopLocked($"按鍵輸出失敗，已停止施放：{failure.Message}");
            }
        }
    }

    /// <summary>
    /// 檢查指定修飾鍵是否已由使用者實體按住。
    /// </summary>
    private bool ModifierHeld(Keys key) => physicalKeys.Contains(key) || key switch
    {
        Keys.ControlKey => physicalKeys.Contains(Keys.LControlKey)
        || physicalKeys.Contains(Keys.RControlKey),
        Keys.ShiftKey => physicalKeys.Contains(Keys.LShiftKey)
        || physicalKeys.Contains(Keys.RShiftKey),
        Keys.Menu => physicalKeys.Contains(Keys.LMenu) || physicalKeys.Contains(Keys.RMenu),
        _ => false
    };

    /// <summary>
    /// 釋放此物件持有的資源，必要時先停止背景工作。
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            StopLocked("技能施放已關閉");
            disposed = true;
            wake.Set();
        }

        if (started)
            worker.Join();
        wake.Dispose();
    }
}

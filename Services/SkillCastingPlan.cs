using WOWCV.Models;

namespace WOWCV.Services;

public sealed record CastingKey(Keys Main, IReadOnlyList<Keys> Modifiers)
{
    public IEnumerable<Keys> KeysInOrder => Modifiers.Append(Main);

    public static CastingKey Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(string.IsNullOrEmpty)) throw new ArgumentException($"無效按鍵：{text}");
        var modifiers = new List<Keys>();
        for (int i = 0; i < parts.Length - 1; i++)
        {
            Keys modifier = parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => Keys.ControlKey,
                "ALT" => Keys.Menu,
                "SHIFT" => Keys.ShiftKey,
                _ => throw new ArgumentException($"不支援的修飾鍵：{parts[i]}")
            };
            if (modifiers.Contains(modifier)) throw new ArgumentException($"重複的修飾鍵：{text}");
            modifiers.Add(modifier);
        }
        string mainText = parts[^1];
        if (mainText.Length == 1 && char.IsAsciiDigit(mainText[0])) mainText = "D" + mainText;
        mainText = mainText.ToUpperInvariant() switch
        {
            "ESC" => "Escape", "ENTER" => "Return",
            "-" => nameof(Keys.OemMinus), "=" => nameof(Keys.Oemplus),
            "[" => nameof(Keys.OemOpenBrackets), "]" => nameof(Keys.OemCloseBrackets),
            "\\" => nameof(Keys.OemPipe), ";" => nameof(Keys.OemSemicolon), "'" => nameof(Keys.OemQuotes),
            "," => nameof(Keys.Oemcomma), "." => nameof(Keys.OemPeriod), "/" => nameof(Keys.OemQuestion),
            "`" => nameof(Keys.Oemtilde), _ => mainText
        };
        if (!Enum.TryParse<Keys>(mainText, true, out var main) || !Enum.IsDefined(main)
            || (int)main < 8 || (int)main > 254 || main == Keys.None || IsModifier(main)
            || mainText.Contains(',') || int.TryParse(mainText, out _))
            throw new ArgumentException($"不支援的鍵盤按鍵：{text}");
        return new(main, modifiers.AsReadOnly());
    }

    public static bool IsModifier(Keys key) => key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
        or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;
}

public sealed record CastingRule(string SkillName, CastingKey Key, int Priority, int Weight, decimal CooldownSeconds,
    string CooldownCheck, int Order);
public sealed record CastingFlow(Keys Trigger, string Label, IReadOnlyList<CastingRule> Rules);
public sealed record SkillCastingPlan(long Revision, IReadOnlyList<CastingFlow> Flows)
{
    public static SkillCastingPlan Compile(SkillDocument document, long revision)
    {
        var bindings = SkillImageBindings.Resolve(document, revision);
        var invalid = bindings.Items.FirstOrDefault(item => item.SkillName.Length > 0 && item.TemplateFile.Length > 0
            && item.CastKey.Length > 0 && item.Issue is not null);
        if (invalid is not null) throw new ArgumentException($"{invalid.SkillName}：{invalid.Issue}");
        if (document.Data.Count == 0 || bindings.Items.Count == 0) throw new ArgumentException("請先匯入含技能的流程。");
        var names = bindings.Items.Select(item => item.SkillName).ToHashSet(StringComparer.Ordinal);
        var triggers = new HashSet<Keys>();
        var flows = new List<CastingFlow>();
        foreach (var group in document.Data)
        {
            var trigger = CastingKey.Parse(group.Press);
            if (trigger.Modifiers.Count != 0) throw new ArgumentException("流程觸發鍵目前僅支援單一鍵盤按鍵。");
            if (!triggers.Add(trigger.Main)) throw new ArgumentException($"流程觸發鍵重複：{group.Press}");
            var rules = new List<CastingRule>();
            foreach (var (skill, index) in group.Process.Select((skill, index) => (skill, index)))
            {
                if (!SkillImageBindings.IsComplete(skill)) continue;
                string check = skill.CooldownCheck.Trim();
                if (check.Length > 0 && !names.Contains(check)) throw new ArgumentException($"{skill.SkillName} 的冷卻檢查技能不存在：{check}");
                CastingKey key;
                try { key = CastingKey.Parse(skill.CastKey); }
                catch (ArgumentException ex) { throw new ArgumentException($"{skill.SkillName} 的施放按鍵「{skill.CastKey}」無效：{ex.Message}", ex); }
                rules.Add(new(skill.SkillName.Trim(), key, skill.Priority, skill.SkillWeight,
                    skill.CdSec, check, index));
            }
            flows.Add(new(trigger.Main, group.Press.Trim(), rules.OrderBy(rule => rule.Priority)
                .ThenByDescending(rule => rule.Weight).ThenBy(rule => rule.Order).ToList().AsReadOnly()));
        }
        if (flows.All(flow => flow.Rules.Count == 0))
            throw new ArgumentException("流程內沒有已完成樣版圖片與施放按鍵綁定的技能。");
        if (flows.SelectMany(flow => flow.Rules).Any(rule => rule.Key.KeysInOrder.Any(triggers.Contains)))
            throw new ArgumentException("施放按鍵不得包含任何流程觸發鍵，請更換觸發鍵或施放鍵。");
        return new(revision, flows.AsReadOnly());
    }
}

/// <summary>Pure selection and session cooldowns; no input or screen access.</summary>
public sealed class SkillDecisionEngine(TimeProvider clock)
{
    private readonly Dictionary<string, double> cooldowns = new(StringComparer.Ordinal);
    private double Seconds(long timestamp) => timestamp / (double)clock.TimestampFrequency;

    public CastingRule? Select(CastingFlow flow, SkillObservationSnapshot snapshot, long now, out string reason)
    {
        var hits = snapshot.Items.Where(item => item.Found && item.Verification.Status == PixelCheckStatus.Passed
                && item.Binding.Issue is null).Select(item => item.Binding.SkillName).ToHashSet(StringComparer.Ordinal);
        double seconds = Seconds(now);
        bool Cooling(string name) => cooldowns.TryGetValue(name, out double until) && until > seconds;
        foreach (var rule in flow.Rules)
        {
            if (!hits.Contains(rule.SkillName) || Cooling(rule.SkillName)
                || (rule.CooldownCheck.Length > 0 && Cooling(rule.CooldownCheck))) continue;
            reason = $"可施放：{rule.SkillName}"; return rule;
        }
        reason = "沒有符合命中與冷卻條件的技能"; return null;
    }

    public void RecordSent(CastingRule rule, long completedAt) =>
        cooldowns[rule.SkillName] = Seconds(completedAt) + (double)rule.CooldownSeconds;
}

/// <summary>Edge-driven: keyboard auto-repeat never changes activation order.</summary>
public sealed class SkillTriggerState(TriggerMode mode, IEnumerable<Keys> triggers)
{
    private readonly HashSet<Keys> accepted = triggers.ToHashSet();
    private readonly List<Keys> held = [];
    private Keys? toggled;
    public Keys? Active => mode == TriggerMode.Toggle ? toggled : held.Count > 0 ? held[^1] : null;
    public void Update(Keys key, bool down)
    {
        if (!accepted.Contains(key)) return;
        if (down)
        {
            if (held.Contains(key)) return;
            held.Add(key);
            if (mode == TriggerMode.Toggle) toggled = toggled == key ? null : key;
        }
        else held.Remove(key);
    }
}

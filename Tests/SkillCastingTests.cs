using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using WOWCV.Controls;
using WOWCV.Models;
using WOWCV.Services;

internal static class SkillCastingTests
{
    /// <summary>
    /// 使用模擬送鍵與時鐘，驗證流程決策、觸發、冷卻、取消及失敗處理。
    /// </summary>
    public static void Run(string root, Action<bool, string> check)
    {
        TestConfiguration(check);
        TestDecisions(check);
        TestPartialBindings(check);
        TestTriggers(check);
        TestTiming(check);
        TestFlowSwitch(check);
        TestCancellation(check);
        TestFailures(check);
        TestUi(root, check);
    }

    /// <summary>
    /// 建立具指定排序與冷卻條件的測試技能。
    /// </summary>
    private static SkillEntry Entry(string name, string key, int priority = 1, int weight = 100,
        decimal cd = 0, string check = "") => new()
    {
        SkillName = name,
        TemplateFile = name + ".png",
        CastKey = key,
        Priority = priority,
        SkillWeight = weight,
        CdSec = cd,
        CooldownCheck = check
    };

    /// <summary>
    /// 建立隔離驗證使用的技能流程文件。
    /// </summary>
    private static SkillDocument Document(params SkillEntry[] rules) => new()
    {
        Data = [
            new()
            {
                Press = "E",
                Process = rules.ToList()
            }
        ]
    };

    /// <summary>
    /// 建立隔離驗證使用的完整技能觀察快照。
    /// </summary>
    private static SkillObservationSnapshot Snapshot(long version, long id, long capture,
        params string[] names) => new(
        version,
        id,
        capture,
        DateTimeOffset.UtcNow,
        capture + 1,
        Array.AsReadOnly(names.Select(name => new SkillObservation(
            new(name, name + ".png", name == "B" ? "2" : "1"),
            new Rectangle(0, 0, 40, 40),
            SkillTrackingState.Matched,
            new(PixelCheckStatus.Passed, 4, [], "4/5"),
            "符合"
        )).ToArray())
    );

    /// <summary>
    /// 驗證設定解析、資料範圍及獨立快照行為。
    /// </summary>
    private static void TestConfiguration(Action<bool, string> check)
    {
        var old = WorkbenchConfigurationStore.Parse("{\"version\":1}");
        check(
            old.Casting is { Mode: TriggerMode.Hold, MinimumIntervalMs: 150, KeyHoldMs: 30 },
            ("Legacy configurations receive hold mode, 150ms global interval and 30ms "
            + "key hold defaults")
        );
        var settings = old with
        {
            Casting = new()
            {
                Mode = TriggerMode.Toggle,
                MinimumIntervalMs = 0,
                KeyHoldMs = 45
            }
        };
        var json = WorkbenchConfigurationStore.Serialize(settings);
        using var parsed = JsonDocument.Parse(json);
        check(
            WorkbenchConfigurationStore.Parse(json) == settings && json.Contains("\"toggle\"")
            && !parsed.RootElement.GetProperty("casting").TryGetProperty("enabled", out _),
            "Casting parameters round-trip without persisting execution state"
        );
        Reject(
            () => WorkbenchConfigurationStore.Parse("{\"version\":1,\"casting\":null}"),
            check,
            "Null casting configuration is rejected"
        );
        Reject(
            () => WorkbenchConfigurationStore.Parse(
            "{\"version\":1,\"casting\":{\"mode\":\"bad\"}}"),
            check,
            "Unknown trigger mode is rejected"
        );
        Reject(
            () => (new CastingConfiguration { MinimumIntervalMs = -1 }).Validate(),
            check,
            "Negative global interval is rejected"
        );
        Reject(
            () => (new CastingConfiguration { KeyHoldMs = 0 }).Validate(),
            check,
            "Zero key hold duration is rejected"
        );
    }

    /// <summary>
    /// 驗證順位、命中與共用冷卻如何影響每輪技能選擇。
    /// </summary>
    private static void TestDecisions(Action<bool, string> check)
    {
        var sorted = SkillCastingPlan.Compile(
            Document(
                Entry("A", "1", 2),
                Entry("B", "2", 1, 5),
                Entry("C", "3", 1, 9),
                Entry("D", "4", 1, 9)
            ),
            3
        );
        check(
            sorted.Revision == 3
            && sorted.Flows[0].Rules.Select(rule => rule.SkillName).SequenceEqual([
                "C",
                "D",
                "B",
                "A"
            ]),
            "Casting ranks priority ascending, weight descending and ties by document order"
        );
        var clock = new FakeClock();
        var engine = new SkillDecisionEngine(clock);
        var document = Document(
            Entry("A", "Ctrl+1", cd: 60),
            Entry("B", "2", weight: 90, check: "A"),
            Entry("C", "3", 2)
        );
        document.Data.Add(new()
        {
            Press = "R",
            Process = [Entry("A", "Ctrl+1", cd: 60)]
        });
        var plan = SkillCastingPlan.Compile(document, 1);
        var hits = Snapshot(1, 1, clock.GetTimestamp(), "A", "B", "C");
        var selected = engine.Select(plan.Flows[0], hits, clock.GetTimestamp(), out _)!;
        check(
            selected.SkillName == "A" && selected.Key.Main == Keys.D1
            && selected.Key.Modifiers.SequenceEqual([Keys.ControlKey]),
            "A verified skill maps to the parsed casting chord"
        );
        engine.RecordSent(selected, clock.GetTimestamp());
        check(
            engine.Select(plan.Flows[0], hits, clock.GetTimestamp(), out _)?.SkillName == "C",
            "Own cooldown and cooldown_check skip A and B, allowing the next eligible skill"
        );
        check(
            engine.Select(plan.Flows[1], hits, clock.GetTimestamp(), out _) is null,
            "Same skill name shares cooldown across E and R flows"
        );
        clock.Advance(60000);
        check(
            engine.Select(plan.Flows[1], hits, clock.GetTimestamp(), out _)?.SkillName == "A",
            "Cooldown expires at its monotonic deadline"
        );
        check(
            engine.Select(plan.Flows[0], Snapshot(1, 2, 1, "B"), clock.GetTimestamp(),
            out _)?.SkillName == "B",
            "Selection considers only skills matched in the complete supplied frame"
        );
        var disabled = hits with
        {
            Items = hits.Items.Select(item => item with { Verification = new(
            PixelCheckStatus.Disabled, 0, [], "off") }).ToArray()
        };
        check(
            engine.Select(plan.Flows[0], disabled, clock.GetTimestamp(), out _) is null,
            "OpenCV-only evidence cannot authorize casting"
        );
        var statusA = Document(Entry("A", "1"));
        statusA.Data[0].Process[0].Status = "A";
        check(
            new SkillDecisionEngine(clock).Select(SkillCastingPlan.Compile(statusA, 1).Flows[0],
            hits, clock.GetTimestamp(), out _) is not null,
            "Reserved STATUS values do not introduce Y/A interpretation"
        );
        check(
            CastingKey.Parse("Alt+Shift+F2").Modifiers.Count == 2
            && CastingKey.Parse("Space").Main == Keys.Space,
            "Keyboard parser supports modifier chords, function keys and named keys"
        );
        var punctuation = new Dictionary<string, Keys>
        {
            ["-"] = Keys.OemMinus,
            ["="] = Keys.Oemplus,
            ["["] = Keys.OemOpenBrackets,
            ["]"] = Keys.OemCloseBrackets,
            ["\\"] = Keys.OemPipe,
            [";"] = Keys.OemSemicolon,
            ["'"] = Keys.OemQuotes,
            [","] = Keys.Oemcomma,
            ["."] = Keys.OemPeriod,
            ["/"] = Keys.OemQuestion,
            ["`"] = Keys.Oemtilde
        };
        check(
            punctuation.All(pair => CastingKey.Parse(pair.Key).Main == pair.Value),
            ("Common unshifted punctuation labels resolve to their physical OEM " + "keyboard keys")
        );
        check(
            CastingKey.Parse("Ctrl+-").Main == Keys.OemMinus
            && CastingKey.Parse("Ctrl+-").Modifiers.SequenceEqual([Keys.ControlKey]),
            "Punctuation keys also work with modifier chords"
        );
        check(
            SkillCastingPlan.Compile(Document(Entry("歐丁之怒", "-")),
            1).Flows[0].Rules[0].Key.Main == Keys.OemMinus,
            "A skill bound to the minus key no longer blocks casting activation"
        );
        try
        {
            SkillCastingPlan.Compile(Document(Entry("錯誤技能", "bad-key")), 1);
            check(false, "Invalid skill key must fail");
        }
        catch (ArgumentException ex)
        {
            check(
                ex.Message.Contains("錯誤技能") && ex.Message.Contains("bad-key"),
                "Invalid casting-key diagnostics identify both the skill and its configured key"
            );
        }

        Reject(
            () => CastingKey.Parse("LButton"),
            check,
            "Mouse keys are excluded from keyboard casting"
        );
        Reject(() => CastingKey.Parse("Ctrl++1"), check, "Malformed key chords are rejected");
        Reject(
            () => SkillCastingPlan.Compile(Document(Entry("A", "E")), 1),
            check,
            "Casting output cannot reuse a flow trigger"
        );
        var duplicate = Document(Entry("A", "1"));
        duplicate.Data.Add(new()
        {
            Press = "e",
            Process = [Entry("B", "2")]
        });
        Reject(
            () => SkillCastingPlan.Compile(duplicate, 1),
            check,
            "Duplicate normalized flow triggers are rejected"
        );
        Reject(
            () => SkillCastingPlan.Compile(Document(Entry("A", "1", check: "missing")), 1),
            check,
            "Unknown cooldown_check names are rejected"
        );
        var conflict = Document(Entry("A", "1"), Entry("A", "2"));
        Reject(
            () => SkillCastingPlan.Compile(conflict, 1),
            check,
            "Conflicting image/key bindings prevent activation"
        );
    }

    /// <summary>
    /// 驗證按住與切換模式的作用中流程及重複按鍵處理。
    /// </summary>
    private static void TestTriggers(Action<bool, string> check)
    {
        var hold = new SkillTriggerState(TriggerMode.Hold, [Keys.E, Keys.R]);
        hold.Update(Keys.E, true);
        hold.Update(Keys.R, true);
        hold.Update(Keys.E, true);
        check(
            hold.Active == Keys.R,
            "Hold mode selects the last physical down, ignoring auto-repeat"
        );
        hold.Update(Keys.R, false);
        check(hold.Active == Keys.E, "Releasing R resumes E when E is still physically held");
        hold.Update(Keys.E, false);
        check(hold.Active is null, "Releasing every hold trigger stops the flow");
        var toggle = new SkillTriggerState(TriggerMode.Toggle, [Keys.E, Keys.R]);
        toggle.Update(Keys.E, true);
        toggle.Update(Keys.E, true);
        toggle.Update(Keys.E, false);
        check(
            toggle.Active == Keys.E,
            "Toggle activation survives key release and ignores auto-repeat"
        );
        toggle.Update(Keys.R, true);
        toggle.Update(Keys.R, false);
        check(toggle.Active == Keys.R, "A different toggle trigger replaces the active flow");
        toggle.Update(Keys.R, true);
        check(toggle.Active is null, "Pressing the active toggle again stops it");
    }

    /// <summary>
    /// 驗證不完整綁定的略過規則及有效綁定的衝突檢查。
    /// </summary>
    private static void TestPartialBindings(Action<bool, string> check)
    {
        var a = Entry("A", "1", check: "B");
        var b = Entry("B", ""); // A known cooldown target need not already be bound for casting.
        var missingImage = Entry("C", "2");
        missingImage.TemplateFile = " ";
        var partialConflict = Entry("D", "");
        partialConflict.TemplateFile = a.TemplateFile;
        var unnamed = Entry(" ", "3");
        var document = Document(a, b, missingImage, partialConflict, unnamed);
        document.Data.Add(new()
        {
            Press = "R",
            Process = [new() { SkillName = "A" }]
        });
        string original = SkillDocumentService.Serialize(document);
        var plan = SkillCastingPlan.Compile(document, 1);
        check(
            plan.Flows[0].Rules.Count == 1 && plan.Flows[0].Rules[0].SkillName == "A",
            ("Incomplete names, templates and casting keys are skipped while bound "
            + "skills remain usable")
        );
        check(
            plan.Flows[1].Rules.Count == 0,
            "An unbound duplicate in another flow neither blocks nor inherits a bound skill"
        );
        check(
            SkillImageBindings.Resolve(document, 1).Items.Single(item => item.SkillName == "A"
            && item.CastKey == "1").Issue is null,
            ("An incomplete template mapping cannot create a conflict against a "
            + "complete binding")
        );
        var clock = new FakeClock();
        check(
            new SkillDecisionEngine(clock).Select(plan.Flows[0], Snapshot(1, 1, 1, "A", "B", "C"),
            clock.GetTimestamp(), out _)?.SkillName == "A",
            ("A complete skill can cast while its known but unbound cooldown target "
            + "has no recorded cooldown")
        );
        check(
            SkillDocumentService.Serialize(document) == original,
            "Skipping incomplete bindings never edits the imported flow document"
        );
        Reject(
            () => SkillCastingPlan.Compile(Document(new SkillEntry { SkillName = "A" }), 1),
            check,
            "An entirely unbound flow still cannot enable casting"
        );
    }

    /// <summary>
    /// 以模擬時鐘驗證完整送鍵後的共用間隔與新影格等待。
    /// </summary>
    private static void TestTiming(Action<bool, string> check)
    {
        var clock = new FakeClock();
        var output = new RecordingInput();
        using var service = new SkillCastingService(output, clock);
        var plan = SkillCastingPlan.Compile(Document(Entry("A", "1"), Entry("B", "2", 2)), 1);
        service.Start();
        service.ConfigureContext(1, true);
        service.Enable(plan, new(), 1);
        service.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = true
        });
        service.PublishSnapshot(Snapshot(1, 1, clock.GetTimestamp() - 1, "A"));
        WaitFor(() => service.Status.FrameId == 1);
        check(
            output.Events.Count == 0,
            "A frame captured before trigger activation cannot start a cast"
        );
        clock.Advance(1);
        service.PublishSnapshot(Snapshot(1, 2, clock.GetTimestamp(), "A", "B"));
        WaitFor(() => output.Events.Count == 1);
        clock.Advance(30);
        service.PublishSnapshot(Snapshot(1, 3, clock.GetTimestamp(), "A"));
        WaitFor(() => service.Status.CompletedCount == 1);
        check(
            output.Events.SequenceEqual(["down:D1", "up:D1"]),
            "Each casting cycle outputs exactly one full skill key press"
        );
        long sentAt = clock.GetTimestamp();
        clock.Advance(149);
        service.PublishSnapshot(Snapshot(1, 4, clock.GetTimestamp(), "B"));
        WaitFor(() => service.Status.FrameId == 4);
        check(
            output.Events.Count == 2 && service.Status.Reason.Contains("共用施放間隔"),
            "B cannot bypass the 150ms shared interval after A"
        );
        clock.Advance(1);
        service.PublishSnapshot(Snapshot(1, 5, sentAt - 1, "A", "B"));
        WaitFor(() => service.Status.FrameId == 5);
        check(
            output.Events.Count == 2 && service.Status.Reason.Contains("新畫面"),
            "Finishing analysis after the interval does not make a pre-send capture fresh"
        );
        clock.Advance(1);
        service.PublishSnapshot(Snapshot(1, 6, clock.GetTimestamp(), "B"));
        WaitFor(() => output.Events.Count == 3);
        clock.Advance(30);
        service.PublishSnapshot(Snapshot(1, 7, clock.GetTimestamp(), "B"));
        WaitFor(() => service.Status.CompletedCount == 2);
        check(
            output.Events.Last() == "up:D2",
            "A fresh complete frame permits B when the global interval has elapsed"
        );
        service.Disable();
        service.Enable(plan, new()
        {
            MinimumIntervalMs = 0
        }, 1);
        service.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = false
        });
        service.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = true
        });
        clock.Advance(1);
        service.PublishSnapshot(Snapshot(1, 8, clock.GetTimestamp(), "A"));
        WaitFor(() => output.Events.Count == 5);
        clock.Advance(30);
        service.PublishSnapshot(Snapshot(1, 9, clock.GetTimestamp(), "A"));
        WaitFor(() => service.Status.CompletedCount == 3);
        service.PublishSnapshot(Snapshot(1, 10, clock.GetTimestamp(), "A"));
        WaitFor(() => service.Status.FrameId == 10);
        check(
            output.Events.Count == 6,
            "Zero interval still cannot reuse a capture from before key release"
        );
        clock.Advance(1);
        service.PublishSnapshot(Snapshot(1, 11, clock.GetTimestamp(), "A"));
        WaitFor(() => output.Events.Count == 7);
        service.Disable();
        WaitFor(() => service.Status.CompletedCount == 4);
        check(
            output.Events.Last() == "up:D1",
            ("A may repeat without a not-matched transition when a new frame and "
            + "cooldown permit it")
        );
        service.ConfigureContext(2, true);
        service.Enable(plan, new()
        {
            MinimumIntervalMs = 0
        }, 2);
        service.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = false
        });
        service.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = true
        });
        clock.Advance(1);
        service.PublishSnapshot(Snapshot(1, 12, clock.GetTimestamp(), "A"));
        service.PublishSnapshot(Snapshot(2, 13, clock.GetTimestamp(), "B"));
        WaitFor(() => output.Events.Count == 9);
        service.Disable();
        WaitFor(() => service.Status.CompletedCount == 5);
        check(
            output.Events.Last() == "up:D2",
            "A previous settings version cannot replace current-version skill evidence"
        );
    }

    /// <summary>
    /// 驗證取消施放會中止等待並釋放本次擁有的按鍵。
    /// </summary>
    private static void TestCancellation(Action<bool, string> check)
    {
        var clock = new FakeClock();
        var output = new RecordingInput();
        using var service = new SkillCastingService(output, clock);
        var plan = SkillCastingPlan.Compile(Document(Entry("A", "Ctrl+1", cd: 60)), 1);
        service.Start();
        service.ConfigureContext(1, true);
        service.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = true
        });
        service.Enable(
            plan,
            new()
            {
                MinimumIntervalMs = 0,
                KeyHoldMs = 5000
            },
            1
        );
        service.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = true
        });
        check(
            service.Status.ActivePress is null,
            "Enabling while a trigger is held requires a new physical down edge"
        );
        service.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = false
        });
        service.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = true
        });
        clock.Advance(1);
        service.PublishSnapshot(Snapshot(1, 1, clock.GetTimestamp(), "A"));
        WaitFor(() => output.Events.Count == 2);
        service.Disable();
        WaitFor(() => output.Events.Count == 4 && service.Status.CompletedCount == 1);
        check(
            output.Events.SequenceEqual([
                "down:ControlKey",
                "down:D1",
                "up:D1",
                "up:ControlKey"
            ]),
            ("Disabling cancels a long key hold and releases only owned keys in " + "reverse order")
        );
        service.Enable(plan, new()
        {
            MinimumIntervalMs = 0
        }, 1);
        service.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = false
        });
        service.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = true
        });
        clock.Advance(1);
        service.PublishSnapshot(Snapshot(1, 2, clock.GetTimestamp(), "A"));
        WaitFor(() => service.Status.FrameId == 2);
        check(
            output.Events.Count == 4 && service.Status.Reason.Contains("冷卻"),
            "Disabling and re-enabling preserves session skill cooldowns"
        );
        service.ConfigureContext(2, false);
        check(
            !service.Status.Enabled && service.Status.ActivePress is null,
            "Context invalidation stops and clears the active flow"
        );
        Reject(
            () => service.Enable(plan, new(), 2),
            check,
            "Static or paused contexts cannot activate casting"
        );
        var heldOutput = new RecordingInput();
        using var held = new SkillCastingService(heldOutput, clock);
        held.Start();
        held.ConfigureContext(1, true);
        held.KeyboardInput(new()
        {
            Key = Keys.LControlKey,
            IsDown = true
        });
        held.Enable(plan, new()
        {
            KeyHoldMs = 5000
        }, 1);
        held.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = true,
            ExtraInformation = WindowsSendInput.InputMarker
        });
        check(
            held.Status.ActivePress is null,
            "Tagged program-generated input cannot activate a flow"
        );
        held.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = true
        });
        clock.Advance(1);
        held.PublishSnapshot(Snapshot(1, 1, clock.GetTimestamp(), "A"));
        WaitFor(() => heldOutput.Events.Count == 1);
        var watch = Stopwatch.StartNew();
        held.Dispose();
        check(
            watch.ElapsedMilliseconds < 1000 && heldOutput.Events.SequenceEqual(["down:D1",
            "up:D1"]),
            ("Shutdown interrupts key holding promptly and never releases a "
            + "physically held modifier")
        );
    }

    /// <summary>
    /// 驗證多個觸發鍵間切換流程時維持共用等待與冷卻。
    /// </summary>
    private static void TestFlowSwitch(Action<bool, string> check)
    {
        var clock = new FakeClock();
        var output = new RecordingInput();
        using var service = new SkillCastingService(output, clock);
        var document = Document(Entry("A", "1"));
        document.Data.Add(new()
        {
            Press = "R",
            Process = [Entry("B", "2")]
        });
        service.Start();
        service.ConfigureContext(1, true);
        service.Enable(SkillCastingPlan.Compile(document, 1), new(), 1);
        service.KeyboardInput(new()
        {
            Key = Keys.E,
            IsDown = true
        });
        clock.Advance(1);
        service.PublishSnapshot(Snapshot(1, 1, clock.GetTimestamp(), "A", "B"));
        WaitFor(() => output.Events.Count == 1);
        clock.Advance(30);
        service.PublishSnapshot(Snapshot(1, 2, clock.GetTimestamp(), "A", "B"));
        WaitFor(() => service.Status.CompletedCount == 1);
        clock.Advance(1);
        service.KeyboardInput(new()
        {
            Key = Keys.R,
            IsDown = true
        });
        clock.Advance(1);
        service.PublishSnapshot(Snapshot(1, 3, clock.GetTimestamp(), "A", "B"));
        WaitFor(() => service.Status.FrameId == 3);
        check(
            service.Status.ActivePress == "R" && output.Events.Count == 2,
            "Switching E to R preserves the existing global wait"
        );
        clock.Advance(150);
        service.PublishSnapshot(Snapshot(1, 4, clock.GetTimestamp(), "A", "B"));
        WaitFor(() => output.Events.Count == 3);
        clock.Advance(30);
        service.PublishSnapshot(Snapshot(1, 5, clock.GetTimestamp(), "A", "B"));
        WaitFor(() => service.Status.CompletedCount == 2);
        check(
            output.Events.Last() == "up:D2",
            "Only R's skill is selected even when E's higher-ranked skill remains matched"
        );
        service.KeyboardInput(new()
        {
            Key = Keys.R,
            IsDown = false
        });
        check(
            service.Status.ActivePress == "E",
            "Releasing R restores E in the live casting coordinator"
        );
        service.ConfigureContext(2, true);
        check(!service.Status.Enabled, "A new recognition context cancels the pending next cast");
    }

    /// <summary>
    /// 模擬按下或放開失敗，確認停止施放並嘗試完成必要釋放。
    /// </summary>
    private static void TestFailures(Action<bool, string> check)
    {
        foreach (bool failOnUp in new[]
        {
            false,
            true
        }
        )
        {
            var clock = new FakeClock();
            var output = new RecordingInput
            {
                FailDown = !failOnUp,
                FailUp = failOnUp
            };
            using var service = new SkillCastingService(output, clock);
            service.Start();
            service.ConfigureContext(1, true);
            service.Enable(SkillCastingPlan.Compile(Document(Entry("A", "Ctrl+1")), 1), new(), 1);
            service.KeyboardInput(new()
            {
                Key = Keys.E,
                IsDown = true
            });
            clock.Advance(1);
            service.PublishSnapshot(Snapshot(1, 1, clock.GetTimestamp(), "A"));
            WaitFor(() => output.Events.Count >= 2);
            clock.Advance(30);
            service.PublishSnapshot(Snapshot(1, 2, clock.GetTimestamp(), "A"));
            WaitFor(() => !service.Status.Enabled);
            check(
                service.Status.CompletedCount == 0 && service.Status.Reason.Contains("輸出失敗")
                && output.Events.Contains("up:D1") && output.Events.Last() == "up:ControlKey",
                failOnUp ? "Key-up failure still attempts every release and stops further casting"
                : "Partial key-down failure releases attempted keys and stops further casting"
            );
        }
    }

    /// <summary>
    /// 驗證施放參數還原、靜態圖片限制及停用條件，使用模擬送鍵。
    /// </summary>
    private static void TestUi(string root, Action<bool, string> check)
    {
        string directory = Path.Combine(root, "casting-ui");
        Directory.CreateDirectory(directory);
        string image = Path.Combine(directory, "A.png"), flow = Path.Combine(directory,
            "flow.json"), config = Path.Combine(directory, "config.json");
        using (var bitmap = new Bitmap(40, 40))
            bitmap.Save(image);
        var document = Document(Entry("A", "1"), Entry("Unbound", ""));
        document.Data.Add(new()
        {
            Press = "R",
            Process = [new() { SkillName = "A" }]
        });
        File.WriteAllText(flow, SkillDocumentService.Serialize(document));
        using (var store = new WorkbenchConfigurationStore(config))
        {
            store.QueueSave(new()
            {
                Source = new()
                {
                    Kind = SourceKind.Image,
                    ImagePath = image,
                    TemplateFolderPath = directory
                },
                SkillFlowPath = flow,
                Casting = new()
                {
                    Mode = TriggerMode.Toggle,
                    MinimumIntervalMs = 250,
                    KeyHoldMs = 45
                }
            });
            store.Flush();
        }

        var output = new RecordingInput();
        using var detection = new DetectionControl(config, output)
        {
            Dock = DockStyle.Fill
        };
        using var host = new Form
        {
            ShowInTaskbar = false,
            Opacity = 0,
            Size = new(1440, 900),
            Location = new(-30000, -30000),
            StartPosition = FormStartPosition.Manual
        };
        host.Controls.Add(detection);
        host.Show();
        detection.StartServices();
        try
        {
            var enable = Find<CheckBox>(detection)
                .Single(control => control.Name == "CastingEnabled");
            var mode = Find<ComboBox>(detection).Single(control => control.Name == "TriggerMode");
            var interval = Find<NumericUpDown>(detection)
                .Single(control => control.Name == "CastingInterval");
            var hold = Find<NumericUpDown>(detection)
                .Single(control => control.Name == "CastingKeyHold");
            var notice = Find<Label>(detection).Single(control => control.Name == "CastingStatus");
            var monitor = Find<SplitContainer>(detection)
                .Single(control => control.Name == "MonitorSplit");
            check(
                !monitor.RectangleToScreen(monitor.ClientRectangle)
                .IntersectsWith(notice.RectangleToScreen(notice.ClientRectangle)),
                "Casting status is visible outside the preview area"
            );
            check(
                !enable.Checked && mode.SelectedIndex == 1 && interval.Value == 250
                && hold.Value == 45,
                "Main UI restores casting parameters while leaving casting disabled"
            );
            Find<Button>(detection).Single(button => button.Text == "開始偵測").PerformClick();
            enable.Checked = true;
            check(
                !enable.Checked && notice.Text.Contains("螢幕來源") && output.Events.IsEmpty,
                "Static-image detection cannot enable actual casting"
            );
            Find<Button>(detection).Single(button => button.Text == "切換螢幕來源").PerformClick();
            enable.Checked = true;
            check(
                enable.Checked && notice.Text.Contains("已啟用"),
                ("Screen casting enables with bound skills despite unbound entries, "
                + "missing unbound images and an unbound second flow")
            );
            Find<Button>(detection).Single(button => button.Text == "重新定位技能").PerformClick();
            check(!enable.Checked, "Relocating skills immediately disables casting");
            enable.Checked = true;
            Find<Button>(detection).Single(button => button.Text == "停止偵測").PerformClick();
            check(!enable.Checked, "Stopping detection also disables casting");
            mode.SelectedIndex = 0;
            interval.Value = 175;
            hold.Value = 35;
            detection.FlushConfiguration();
            var saved = WorkbenchConfigurationStore.Parse(File.ReadAllText(config));
            check(
                saved.Casting is { Mode: TriggerMode.Hold, MinimumIntervalMs: 175, KeyHoldMs: 35 }
                && saved.SkillFlowPath == flow && output.Events.IsEmpty,
                "Casting UI saves only parameters and never sends native keys in validation"
            );
            using var screenshot = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
            screenshot.Save(Path.Combine(directory, "casting-controls.png"));
        }
        finally
        {
            detection.StopServices();
            host.Close();
        }
    }

    /// <summary>
    /// 遞迴找出指定型別的子控制項，供介面驗證定位。
    /// </summary>
    private static IEnumerable<T> Find<T>(Control parent)
        where T : Control => parent.Controls.Cast<Control>().SelectMany(child => (child is T match
        ? new[] { match } : []).Concat(Find<T>(child)));

    /// <summary>
    /// 確認無效操作被拒絕且回報預期的設定錯誤。
    /// </summary>
    private static void Reject(Action action, Action<bool, string> check, string message)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException
            or InvalidOperationException)
        {
            check(true, message);
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>
    /// 在有限等待時間內處理介面訊息並等待指定測試條件成立。
    /// </summary>
    private static void WaitFor(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 5000)
        {
            Application.DoEvents();
            if (condition())
                return;
            Thread.Sleep(2);
        }

        throw new InvalidOperationException("Timed out waiting for casting verification.");
    }

    private sealed class FakeClock : TimeProvider
    {
        private long timestamp = 1000;
        public override long TimestampFrequency => 1000;

        /// <summary>
        /// 回傳測試控制的單調時鐘時間戳。
        /// </summary>
        public override long GetTimestamp() => Interlocked.Read(ref timestamp);

        /// <summary>
        /// 推進模擬時鐘，讓冷卻與等待測試不依賴真實時間。
        /// </summary>
        public void Advance(int ms) => Interlocked.Add(ref timestamp, ms);
    }

    private sealed class RecordingInput : ISendInput
    {
        public ConcurrentQueue<string> Events { get; } = new();
        public bool FailDown
        {
            get; init;
        }
        public bool FailUp
        {
            get; init;
        }

        /// <summary>
        /// 記錄模擬按下事件，依測試設定回報失敗或喚醒等待條件。
        /// </summary>
        public void KeyDown(Keys key)
        {
            Events.Enqueue("down:" + key);
            if (FailDown && key == Keys.D1)
                throw new IOException("test down failure");
        }

        /// <summary>
        /// 記錄模擬放開事件，供釋放順序與失敗處理驗證。
        /// </summary>
        public void KeyUp(Keys key)
        {
            Events.Enqueue("up:" + key);
            if (FailUp && key == Keys.D1)
                throw new IOException("test up failure");
        }

        /// <summary>
        /// 拒絕此模擬服務不支援的滑鼠按下操作。
        /// </summary>
        public void MouseDown(MouseButtons button) => throw new NotSupportedException();

        /// <summary>
        /// 拒絕此模擬服務不支援的滑鼠放開操作。
        /// </summary>
        public void MouseUp(MouseButtons button) => throw new NotSupportedException();

        /// <summary>
        /// 拒絕此模擬服務不支援的滑鼠移動操作。
        /// </summary>
        public void MoveMouse(int deltaX, int deltaY) => throw new NotSupportedException();

        /// <summary>
        /// 拒絕此模擬服務不支援的滾輪操作。
        /// </summary>
        public void ScrollMouse(int delta,
            bool horizontal = false) => throw new NotSupportedException();
    }
}

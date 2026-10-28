using WinKbdCheck;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); passed++; Console.WriteLine("PASS: " + name); }
long now = 0;
var session = new TestSession(() => now);
KeyInput Input(int vk, uint scan, bool down, bool ext = false, bool injected = false) => new(vk, scan, ext, down, injected, now, DateTime.Now);
Check(KeyboardLayout.Keys.Count == 104, "104 distinct key definitions");
Check(KeyboardLayout.Keys.Select(k => k.Id).Distinct().Count() == 104, "unique key IDs");
Check(KeyboardLayout.Match(0xA2, 0x1D, false)?.Name == "左 Ctrl", "hook VK_LCONTROL");
Check(KeyboardLayout.Match(0xA3, 0x1D, true)?.Name == "右 Ctrl", "hook VK_RCONTROL");
Check(KeyboardLayout.Match(0xA5, 0x38, true)?.Name == "右 Alt (AltGr)", "hook VK_RMENU");
Check(KeyboardLayout.Match(0xA1, 0x36, false)?.Name == "右 Shift", "right Shift");
Check(KeyboardLayout.Match(0x0D, 0x1C, true)?.Name == "小键盘 回车", "numpad Enter distinct");
Check(KeyboardLayout.Match(0x0D, 0x1C, false)?.Name == "回车键（主键区）", "main Enter distinct");
Check(KeyboardLayout.Match(0x24, 0x47, false)?.Vk == 0x67, "NumLock off: numpad 7 is not Home");
Check(KeyboardLayout.Match(0x24, 0x47, true)?.Vk == 0x24, "extended Home is not numpad");
Check(KeyboardLayout.Match(0x5B, 0x5B, true)?.Name == "左 Windows 键", "left Win");
Check(KeyboardLayout.Match(0x5C, 0x5C, true)?.Name == "右 Windows 键", "right Win");
session.Start();
Check(session.Target.Def.Vk == 0x1B, "guide starts with Esc");
session.Handle(Input(0x1B, 1, true, injected: true));
Check(session.Responded == 0 && session.IgnoredInjected == 1, "synthetic input cannot pass hardware test");
session.Handle(Input(0x1B, 1, true));
Check(session.Target.Def.Vk == 0x1B && session.Keys.First(k => k.Def.Vk == 0x1B).IsDown, "press feedback, await release");
now = 5000; session.Tick();
Check(session.Target.Def.Vk == 0x1B, "held key is not skipped for not being pressed");
session.Handle(Input(0x1B, 1, false));
Check(session.Responded == 1 && session.Target.Def.Vk == 0x70, "release confirms and advances");
now += 2999; session.Tick(); Check(session.Target.Def.Vk == 0x70, "deadline is not early");
now++; session.Tick(); Check(session.Skipped == 1 && session.Target.Def.Vk == 0x71, "3s timeout advances");
session.Handle(Input(0x70, 0x3B, true)); now += 50; session.Handle(Input(0x70, 0x3B, false));
Check(session.Responded == 2 && session.Skipped == 0 && session.Target.Def.Vk == 0x71, "late recovery updates same record");
session.Start();
session.Handle(Input(0x0D, 0x1C, true)); now += 100;
session.Handle(Input(0x0D, 0x1C, true));
Check(session.Running, "auto-repeat is not double Enter");
session.Handle(Input(0x0D, 0x1C, false)); now += 100;
session.Handle(Input(0x0D, 0x1C, true)); now += 50; session.Handle(Input(0x0D, 0x1C, false));
Check(!session.Running && session.FinishReason.Contains("双击"), "two complete Enter presses finish");
session.Start();
session.Handle(Input(0x0D, 0x1C, true)); session.Handle(Input(0x0D, 0x1C, false)); now += 100;
session.Handle(Input(0x0D, 0x1C, true, true)); session.Handle(Input(0x0D, 0x1C, false, true));
Check(session.Running, "different Enter keys are not double-click");
session.Start(true); Check(session.Required == 87, "TKL excludes only numpad");
session.Start();
foreach (var def in KeyboardLayout.Keys)
{
    session.Handle(Input(def.Vk, (uint)def.ScanCode, true, def.ExtMode == 1)); now += 600;
    session.Handle(Input(def.Vk, (uint)def.ScanCode, false, def.ExtMode == 1));
}
Check(!session.Running && session.Responded == 104, "all 104 press/release cycles finish the queue");
session.Start(); Check(session.Responded == 0 && session.Skipped == 0, "reset clears all result state");
session.Handle(Input(0x41, 0x1E, true)); session.Stop("manual");
Check(session.Keys.First(k => k.Def.Vk == 0x41).Result == KeyResult.ReleaseMissing, "finish never fabricates release");
Console.WriteLine($"All {passed} regression checks passed.");
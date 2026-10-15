using System.Text.Json;
using BingLan.Core.Models;
using BingLan.Core.Services;
using BingLan.Core.Themes;
using BingLan.Core.TopBar;

namespace BingLan.SmokeTests;

/// <summary>Search matching, pinyin initials, hotkey parsing and the v29 migration.</summary>
public static class TopBarSearchTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Run()
    {
        TestPinyinInitials();
        TestMatchingAndRanking();
        TestHotkeyParsing();
        TestSearchPersistenceAndMigration();
        TestThemeCarriesSearchSwitch();
    }

    private static void TestPinyinInitials()
    {
        Assert(TopBarSearchRules.PinyinInitials("微信") == "wx", "微信首字母 wx");
        Assert(TopBarSearchRules.PinyinInitials("网易云音乐") == "wyyyl", "网易云音乐首字母 wyyyl");
        Assert(TopBarSearchRules.PinyinInitials("QQ音乐") == "qqyl",
            "中英混合首字母拼接（乐取最常见读音 l）");
        Assert(TopBarSearchRules.PinyinInitials("Microsoft Edge") == "microsoftedge",
            "ASCII 字母数字原样保留");
        Assert(TopBarSearchRules.PinyinInitials("·— ") == string.Empty, "标点空白跳过");
        Assert(TopBarSearchRules.PinyinInitials("") == string.Empty, "空串安全");
    }

    private static void TestMatchingAndRanking()
    {
        var wechat = new TopBarSearchCandidate(
            TopBarSearchItemKind.App, "微信", "微信.lnk", @"C:\Users\a\Desktop\微信.lnk");
        var wechatExe = new TopBarSearchCandidate(
            TopBarSearchItemKind.App, "微信", "Weixin.exe", @"C:\Program Files\Tencent\Weixin.exe");
        var report = new TopBarSearchCandidate(
            TopBarSearchItemKind.File, "季度报告", "季度报告.docx", @"C:\Users\a\Desktop\季度报告.docx");
        var cloudFolder = new TopBarSearchCandidate(
            TopBarSearchItemKind.Folder, "云盘资料", "云盘资料", @"C:\Users\a\Desktop\云盘资料");
        var candidates = new[] { wechat, wechatExe, report, cloudFolder };

        var exact = TopBarSearchRules.Search(candidates, "微信");
        Assert(exact.Count >= 2 && exact[0].DisplayName == "微信", "名称匹配命中");
        // An exactly-named entry outranks one that merely contains the query; both are
        // files here so the app boost stays out of the comparison.
        var notes = new TopBarSearchCandidate(
            TopBarSearchItemKind.File, "微信", "微信.txt", @"C:\d\微信.txt");
        var archive = new TopBarSearchCandidate(
            TopBarSearchItemKind.File, "微信备份", "微信备份.zip", @"C:\d\微信备份.zip");
        var byName = TopBarSearchRules.Search(new[] { archive, notes }, "微信");
        Assert(byName.Count == 2 && byName[0].Path == notes.Path, "精确名称排在包含匹配前");

        // A query token that is a whole word of the name outranks a substring hit;
        // both entries are files so the app boost stays out of the comparison.
        var wordHit = new TopBarSearchCandidate(
            TopBarSearchItemKind.File, "Google Chrome", "Google Chrome.html", @"C:\d\chrome.html");
        var substringHit = new TopBarSearchCandidate(
            TopBarSearchItemKind.File, "Chromebook 指南", "Chromebook 指南.html", @"C:\d\book.html");
        var byWord = TopBarSearchRules.Search(new[] { substringHit, wordHit }, "chrome");
        Assert(byWord.Count == 2 && byWord[0].Path == wordHit.Path, "整词命中排在包含命中前");

        // Typos in longer tokens still hit at the lowest rung; short tokens and far
        // strings stay unmatched so the tolerance cannot flood the list with guesses.
        var chrome = new TopBarSearchCandidate(
            TopBarSearchItemKind.File, "Chrome", "Chrome.exe", @"C:\d\Chrome.exe");
        Assert(TopBarSearchRules.Search(new[] { chrome }, "chorme").Count == 1, "较长词 1 字之差容错命中");
        var shortWord = new TopBarSearchCandidate(
            TopBarSearchItemKind.File, "Cat", "Cat.txt", @"C:\d\Cat.txt");
        Assert(TopBarSearchRules.Search(new[] { shortWord }, "cot").Count == 0, "短词即使只差一个字符也不做容错");
        Assert(TopBarSearchRules.Search(new[] { chrome }, "chzzzzzz").Count == 0, "距离过远不容错");
        var mixed = TopBarSearchRules.Search(new[] { wordHit, substringHit, chrome }, "google chorme");
        Assert(mixed.Count == 1 && mixed[0].Path == wordHit.Path, "容错词与整词组合命中");

        var typoApp = new TopBarSearchCandidate(
            TopBarSearchItemKind.App, "Google Chome", "Google Chome.lnk", @"C:\d\Chome.lnk");
        var normal = TopBarSearchRules.Search(new[] { typoApp, substringHit }, "chrome");
        Assert(normal.Count == 1 && normal[0].Path == substringHit.Path,
            "存在正常结果时不混入容错结果，也不补满列表");
        var normalMulti = TopBarSearchRules.Search(new[] { wordHit, typoApp }, "google chrome");
        Assert(normalMulti.Count == 1 && normalMulti[0].Path == wordHit.Path,
            "多关键词正常命中时不启用容错");

        var setup = new TopBarSearchCandidate(
            TopBarSearchItemKind.File, "Google ChromeSetup", "Google ChromeSetup.exe", @"C:\d\setup.exe");
        foreach (var typo in new[] { "chorme", "chrone", "chrme", "chxrome" })
        {
            var fallback = TopBarSearchRules.Search(new[] { setup }, "google " + typo);
            Assert(fallback.Count == 1, $"长单词前缀支持交换、替换、缺字和多字：{typo}");
        }
        var longWord = new TopBarSearchCandidate(
            TopBarSearchItemKind.File, "NotepadSetup", "NotepadSetup.exe", @"C:\d\notepad.exe");
        Assert(TopBarSearchRules.Search(new[] { longWord }, "nxtxpad").Count == 1,
            "七字符词允许两次编辑");
        Assert(TopBarSearchRules.Search(new[] { longWord }, "nxxxpad").Count == 0,
            "七字符词超过两次编辑不命中");
        Assert(TopBarSearchRules.Search(new[] { chrome }, "chxxme").Count == 0,
            "六字符词超过一次编辑不命中");
        Assert(TopBarSearchRules.Search(new[] { setup }, "missing chorme").Count == 0,
            "容错阶段仍要求所有关键词命中");

        var initials = TopBarSearchRules.Search(candidates, "wx");
        Assert(initials.Any(item => item.DisplayName == "微信"), "拼音首字母 wx 命中微信");
        Assert(initials.All(item => item.Path != report.Path), "首字母不误伤无关注果");

        var folderInitials = TopBarSearchRules.Search(candidates, "ypzl");
        Assert(folderInitials.Count == 1 && folderInitials[0].Path == cloudFolder.Path,
            "拼音首字母命中文件夹");

        var fileName = TopBarSearchRules.Search(candidates, "weixin");
        Assert(fileName.Count == 1 && fileName[0].Path == wechatExe.Path, "完整文件名包含命中");

        // Apps lead files at equal match quality: the launcher reading wins.
        var backup = new TopBarSearchCandidate(
            TopBarSearchItemKind.File, "微信备份", "微信备份.zip", @"C:\Users\a\Desktop\微信备份.zip");
        var appsFirst = TopBarSearchRules.Search(new[] { backup, wechat }, "微信");
        Assert(appsFirst[0].Kind == TopBarSearchItemKind.App, "应用加权排在文件前");

        // Multi-token search keeps only candidates that hit every token.
        var both = TopBarSearchRules.Search(candidates, "微信 weixin");
        Assert(both.All(item => item.DisplayName == "微信"), "多关键词全部命中才保留");
        Assert(TopBarSearchRules.Search(candidates, "微信 不存在词").Count == 0,
            "任一关键词落空则整条落空");
        Assert(TopBarSearchRules.Search(candidates, "   ").Count == 0, "空关键词不出结果");

        var many = Enumerable.Range(0, 100)
            .Select(index => new TopBarSearchCandidate(
                TopBarSearchItemKind.File, $"记事本{index}", $"记事本{index}.txt", $"C:\\d\\{index}.txt"))
            .ToArray();
        Assert(TopBarSearchRules.Search(many, "记事本").Count == TopBarSearchRules.MaxResults,
            "结果条数封顶");
    }

    private static void TestHotkeyParsing()
    {
        var binding = TopBarSearchRules.ParseHotkey(TopBarState.DefaultSearchHotkey);
        Assert(binding is not null, "默认热键可解析");
        Assert(binding!.Modifiers ==
            (TopBarSearchRules.ModifierControl | TopBarSearchRules.ModifierAlt), "默认热键修饰键");
        Assert(binding.VirtualKey == 'F', "默认热键主键");
        Assert(TopBarSearchRules.FormatHotkey(binding) == "Ctrl+Alt+F", "规范文本往返一致");

        Assert(TopBarSearchRules.ParseHotkey("Ctrl+Alt+Space")?.VirtualKey == 0x20, "空格键名");
        Assert(TopBarSearchRules.ParseHotkey("Alt+F4")?.VirtualKey == 0x73, "F4 功能键");
        Assert(TopBarSearchRules.ParseHotkey("Ctrl+0")?.VirtualKey == '0', "数字键");
        Assert(TopBarSearchRules.ParseHotkey(" ctrl + shift + a ") is not null, "容忍空白与大小写");

        Assert(TopBarSearchRules.ParseHotkey(null) is null, "空值不可用");
        Assert(TopBarSearchRules.ParseHotkey("") is null, "空串不可用");
        Assert(TopBarSearchRules.ParseHotkey("F") is null, "裸键会吞掉打字，必须有修饰键");
        Assert(TopBarSearchRules.ParseHotkey("Ctrl") is null, "只有修饰键不可用");
        Assert(TopBarSearchRules.ParseHotkey("Ctrl+Foo") is null, "未知键名不可用");
        Assert(TopBarSearchRules.ParseHotkey("Ctrl+A+B") is null, "两个主键不可用");
        Assert(!TopBarSearchRules.IsValidHotkey("乱写"), "无效文本被识别");
    }

    private static void TestSearchPersistenceAndMigration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "binglan-search-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new LocalStateStore(directory);
            var state = store.Load();
            state.TopBar.IsEnabled = true;
            state.TopBar.SearchHotkey = "Alt+Q";
            state.TopBar.UseEverything = true;
            state.TopBar.SearchPresentation = SearchPresentation.Floating;
            state.TopBar.SearchWindowWidth = 560;
            state.TopBar.SearchSurfaceColor = "#F3F6FC";
            state.TopBar.SearchSurfaceOpacity = 0.7;
            state.TopBar.SearchCornerRadius = 16;
            state.TopBar.SearchWindowPlacement.Left = 180;
            store.Save(state);

            var reloaded = store.Load();
            Assert(reloaded.TopBar.SearchSurfaceColor == "#F3F6FC" && reloaded.TopBar.SearchSurfaceOpacity == 0.7
                && reloaded.TopBar.SearchCornerRadius == 16, "独立搜索框外观应持久化");
            Assert(reloaded.TopBar.SearchHotkey == "Alt+Q", "热键持久化往返一致");
            Assert(reloaded.TopBar.UseEverything && reloaded.TopBar.SearchPresentation == SearchPresentation.Floating
                && reloaded.TopBar.SearchWindowWidth == 560 && reloaded.TopBar.SearchWindowPlacement.Left == 180,
                "搜索来源、入口和布局应持久保存");
            Assert(TopBarRules.IsModuleOn(reloaded.TopBar.Modules, TopBarModuleKind.Search),
                "搜索模块默认开启");
            reloaded.TopBar.SearchHotkey = "乱写";
            TopBarRules.Normalize(reloaded.TopBar);
            Assert(reloaded.TopBar.SearchHotkey == TopBarState.DefaultSearchHotkey,
                "无效热键文本回落默认");

            // A v28 file predates the search box: no module switch, no hotkey text.
            var v28 = JsonSerializer.SerializeToNode(state);
            v28!["SchemaVersion"] = 28;
            v28["TopBar"] = JsonSerializer.SerializeToNode(new Dictionary<string, object?>
            {
                ["IsEnabled"] = true,
                ["Modules"] = new Dictionary<string, object?> { ["Clock"] = false }
            });
            File.WriteAllText(
                Path.Combine(directory, "widgets.json"),
                v28.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            var migrated = store.Load();
            Assert(migrated.SchemaVersion == AppState.CurrentSchemaVersion, "v28 迁移到当前版本");
            Assert(migrated.TopBar.Modules.Search, "迁移后搜索模块默认开启");
            Assert(!migrated.TopBar.Modules.Clock, "迁移保留原有模块开关");
            Assert(migrated.TopBar.SearchHotkey == TopBarState.DefaultSearchHotkey,
                "迁移后热键取默认值");
            Assert(!migrated.TopBar.UseEverything && migrated.TopBar.SearchPresentation == SearchPresentation.TopBar,
                "旧配置应保留顶栏居中并默认关闭 Everything");
            migrated.TopBar.SearchWindowWidth = double.PositiveInfinity;
            migrated.TopBar.SearchPresentation = (SearchPresentation)99;
            TopBarRules.Normalize(migrated.TopBar);
            Assert(migrated.TopBar.SearchWindowWidth == 420 && migrated.TopBar.SearchPresentation == SearchPresentation.TopBar,
                "损坏的搜索宽度与显示方式应恢复默认");
            var v29 = JsonSerializer.SerializeToNode(state)!;
            v29["SchemaVersion"] = 29;
            var oldTopBar = v29["TopBar"]!.AsObject();
            foreach (var field in new[] { "UseEverything", "SearchPresentation", "SearchWindowWidth", "SearchWindowPlacement" })
            {
                oldTopBar.Remove(field);
            }
            File.WriteAllText(Path.Combine(directory, "widgets.json"), v29.ToJsonString());
            var upgraded = store.Load();
            Assert(upgraded.SchemaVersion == AppState.CurrentSchemaVersion && upgraded.TopBar.SearchHotkey == "Alt+Q"
                && !upgraded.TopBar.UseEverything && upgraded.TopBar.SearchWindowWidth == 420,
                "v29 升级应保留原热键，并使用可选搜索来源和布局的默认值");
            var v30 = JsonSerializer.SerializeToNode(state)!;
            v30["SchemaVersion"] = 30;
            foreach (var field in new[] { "SearchFollowCardLook", "SearchSurfaceColor", "SearchSurfaceOpacity", "SearchCornerRadius" })
                v30["TopBar"]!.AsObject().Remove(field);
            File.WriteAllText(Path.Combine(directory, "widgets.json"), v30.ToJsonString());
            var withAppearance = store.Load();
            Assert(withAppearance.SchemaVersion == AppState.CurrentSchemaVersion && withAppearance.TopBar.SearchWindowWidth == 560
                && withAppearance.TopBar.SearchSurfaceColor == TopBarState.DefaultSurfaceColor
                && withAppearance.TopBar.SearchCornerRadius == 10, "v30 应保留布局并迁移搜索外观默认值");
            withAppearance.TopBar.SearchSurfaceColor = "invalid";
            withAppearance.TopBar.SearchSurfaceOpacity = double.NaN;
            withAppearance.TopBar.SearchCornerRadius = double.PositiveInfinity;
            TopBarRules.Normalize(withAppearance.TopBar);
            Assert(withAppearance.TopBar.SearchSurfaceColor == TopBarState.DefaultSurfaceColor
                && withAppearance.TopBar.SearchSurfaceOpacity == 0.94 && withAppearance.TopBar.SearchCornerRadius == 10,
                "损坏的搜索外观应恢复安全默认值");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void TestThemeCarriesSearchSwitch()
    {
        var theme = new ThemePackage();
        Assert(theme.Bindings.TopBarModules is null, "主题包默认不携带模块开关");
        theme.Bindings.TopBarModules = new TopBarModuleSwitches { Search = false };
        Assert(theme.Bindings.TopBarModules.Search == false, "主题包携带搜索开关");
        var personal = new TopBarState
        {
            UseEverything = true,
            SearchPresentation = SearchPresentation.Floating,
            SearchWindowWidth = 520,
            SearchSurfaceColor = "#F3F6FC",
            SearchCornerRadius = 16
        };
        ThemeRules.ApplyTopBar(theme, personal);
        Assert(personal.UseEverything && personal.SearchPresentation == SearchPresentation.Floating
            && personal.SearchWindowWidth == 520 && personal.SearchSurfaceColor == "#F3F6FC" && personal.SearchCornerRadius == 16,
            "应用主题应保留本机搜索来源、入口布局和外观");
    }
}

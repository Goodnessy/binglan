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
            store.Save(state);

            var reloaded = store.Load();
            Assert(reloaded.TopBar.SearchHotkey == "Alt+Q", "热键持久化往返一致");
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
    }
}

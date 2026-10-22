using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CompareImage.Models;

namespace CompareImage.Services;

public class PairMatchingRule
{
    public string DisplayName { get; set; } = string.Empty;
    public Func<string, string, List<ImagePair>> MatcherFunc { get; set; }

    public PairMatchingRule(string name, Func<string, string, List<ImagePair>> func)
    {
        DisplayName = name;
        MatcherFunc = func;
    }
}

public class PairMatchingService
{
    public ObservableCollection<PairMatchingRule> AvailableRules { get; } = new();

    public PairMatchingService()
    {
        // 組み込みルール 1: 同名ファイル一致
        AvailableRules.Add(new PairMatchingRule("同名ファイル一致 (.png ファイル名が同一)", MatchBySameName));

        // 組み込みルール 2: ファイル名順ソート一致 (連番対応)
        AvailableRules.Add(new PairMatchingRule("名前順ソート順一致 (連番・撮影順)", MatchBySortedOrder));

        // 組み込みルール 3: ファイル名から数値を抽出して一致
        AvailableRules.Add(new PairMatchingRule("ファイル名ID一致 (正規表現で数値を抽出)", MatchByNumericId));
    }

    /// <summary>
    /// 外部からユーザー定義のマッチングルールを動的に追加できるメソッド
    /// </summary>
    public void RegisterCustomRule(string name, Func<string, string, List<ImagePair>> customFunc)
    {
        AvailableRules.Add(new PairMatchingRule(name, customFunc));
    }

    public void RegisterRule(PairMatchingRule rule)
    {
        AvailableRules.Add(rule);
    }

    public List<ImagePair> CreatePairs(string baseDir, string targetDir, Func<string, string, List<ImagePair>>? matcherFunc = null)
    {
        if (!Directory.Exists(baseDir)) throw new DirectoryNotFoundException($"ベースフォルダが見つかりません: {baseDir}");
        if (!Directory.Exists(targetDir)) throw new DirectoryNotFoundException($"ターゲットフォルダが見つかりません: {targetDir}");

        var func = matcherFunc ?? MatchBySameName;
        return func(baseDir, targetDir);
    }

    public static List<ImagePair> MatchBySameName(string baseDir, string targetDir)
    {
        var baseFiles = Directory.GetFiles(baseDir, "*.png", SearchOption.TopDirectoryOnly);
        var targetFileMap = Directory.GetFiles(targetDir, "*.png", SearchOption.TopDirectoryOnly)
            .ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);

        var pairs = new List<ImagePair>();
        foreach (var bFile in baseFiles)
        {
            var fileName = Path.GetFileName(bFile);
            if (targetFileMap.TryGetValue(fileName, out var tFile))
            {
                pairs.Add(new ImagePair
                {
                    BaseImagePath = bFile,
                    TargetImagePath = tFile,
                    PairName = Path.GetFileNameWithoutExtension(fileName)
                });
            }
        }
        return pairs;
    }

    public static List<ImagePair> MatchBySortedOrder(string baseDir, string targetDir)
    {
        var baseFiles = Directory.GetFiles(baseDir, "*.png", SearchOption.TopDirectoryOnly).OrderBy(f => f).ToList();
        var targetFiles = Directory.GetFiles(targetDir, "*.png", SearchOption.TopDirectoryOnly).OrderBy(f => f).ToList();

        var pairs = new List<ImagePair>();
        int count = Math.Min(baseFiles.Count, targetFiles.Count);
        for (int i = 0; i < count; i++)
        {
            var bFile = baseFiles[i];
            var tFile = targetFiles[i];
            pairs.Add(new ImagePair
            {
                BaseImagePath = bFile,
                TargetImagePath = tFile,
                PairName = $"Pair_{i + 1:D3} ({Path.GetFileName(bFile)} ⇔ {Path.GetFileName(tFile)})"
            });
        }
        return pairs;
    }

    public static List<ImagePair> MatchByNumericId(string baseDir, string targetDir)
    {
        var baseFiles = Directory.GetFiles(baseDir, "*.png", SearchOption.TopDirectoryOnly);
        var targetFiles = Directory.GetFiles(targetDir, "*.png", SearchOption.TopDirectoryOnly);

        string? ExtractId(string path)
        {
            var m = Regex.Match(Path.GetFileName(path), @"\d+");
            return m.Success ? m.Value : null;
        }

        var targetMap = new Dictionary<string, string>();
        foreach (var tf in targetFiles)
        {
            var id = ExtractId(tf);
            if (id != null && !targetMap.ContainsKey(id))
            {
                targetMap[id] = tf;
            }
        }

        var pairs = new List<ImagePair>();
        foreach (var bf in baseFiles)
        {
            var id = ExtractId(bf);
            if (id != null && targetMap.TryGetValue(id, out var tf))
            {
                pairs.Add(new ImagePair
                {
                    BaseImagePath = bf,
                    TargetImagePath = tf,
                    PairName = $"ID_{id} ({Path.GetFileName(bf)} ⇔ {Path.GetFileName(tf)})"
                });
            }
        }
        return pairs;
    }
}

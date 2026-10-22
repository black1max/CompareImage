using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using CompareImage.Models;

namespace CompareImage.Services;

public class RoslynRuleLoadResult
{
    public List<PairMatchingRule> LoadedRules { get; } = new();
    public List<string> Errors { get; } = new();
}

public class RoslynRuleLoaderService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// JSONファイルから複数のスクリプトルールを読み込み、RoslynでコンパイルしてPairMatchingRuleリストを生成する
    /// </summary>
    public static async Task<RoslynRuleLoadResult> LoadRulesFromJsonAsync(string jsonPath)
    {
        var result = new RoslynRuleLoadResult();

        if (!File.Exists(jsonPath))
        {
            result.Errors.Add($"指定された設定ファイルが存在しません: {jsonPath}");
            return result;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(jsonPath);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"ファイルの読み込みに失敗しました: {ex.Message}");
            return result;
        }

        List<CustomPairRuleDefinition> definitions = new();

        try
        {
            // 配列形式 [ { ... } ] か オブジェクト形式 { "rules": [ ... ] } の両方に対応
            if (json.TrimStart().StartsWith("["))
            {
                definitions = JsonSerializer.Deserialize<List<CustomPairRuleDefinition>>(json, _jsonOptions) ?? new();
            }
            else
            {
                var configFile = JsonSerializer.Deserialize<CustomPairRuleConfigFile>(json, _jsonOptions);
                if (configFile?.Rules != null)
                {
                    definitions = configFile.Rules;
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"JSONのパースに失敗しました: {ex.Message}");
            return result;
        }

        // Roslyn スクリプト実行環境のセットアップ
        var options = ScriptOptions.Default
            .WithReferences(
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(System.Text.RegularExpressions.Regex).Assembly,
                typeof(Path).Assembly,
                typeof(ImagePair).Assembly
            )
            .WithImports(
                "System",
                "System.IO",
                "System.Collections.Generic",
                "System.Linq",
                "System.Text.RegularExpressions",
                "CompareImage.Models"
            );

        foreach (var def in definitions)
        {
            if (string.IsNullOrWhiteSpace(def.Name) || string.IsNullOrWhiteSpace(def.Script))
            {
                result.Errors.Add($"ルール名またはスクリプトが空の項目があります。");
                continue;
            }

            try
            {
                string scriptCode = def.Script.Trim();

                // ラムダ式として書かれていない場合は (baseDir, targetDir) => { ... } でラップ
                if (!scriptCode.StartsWith("(") && !scriptCode.Contains("=>"))
                {
                    scriptCode = $"return new System.Func<string, string, System.Collections.Generic.List<CompareImage.Models.ImagePair>>((baseDir, targetDir) => {{\n{scriptCode}\n}});";
                }
                else if (!scriptCode.StartsWith("return "))
                {
                    scriptCode = $"return {scriptCode};";
                }

                // Roslyn でコンパイル・評価
                var compiledFunc = await CSharpScript.EvaluateAsync<Func<string, string, List<ImagePair>>>(scriptCode, options);

                if (compiledFunc != null)
                {
                    string displayName = string.IsNullOrEmpty(def.Description)
                        ? $"[Roslyn] {def.Name}"
                        : $"[Roslyn] {def.Name} ({def.Description})";

                    result.LoadedRules.Add(new PairMatchingRule(displayName, compiledFunc));
                }
                else
                {
                    result.Errors.Add($"ルール「{def.Name}」: 関数の生成に失敗しました (null)。");
                }
            }
            catch (CompilationErrorException cex)
            {
                string diag = string.Join("\n", cex.Diagnostics.Select(d => $"・行{d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}"));
                result.Errors.Add($"ルール「{def.Name}」コンパイルエラー:\n{diag}");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"ルール「{def.Name}」実行エラー: {ex.Message}");
            }
        }

        return result;
    }
}

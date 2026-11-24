using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 把「模板場景用不到」的資源從 Assets 搬到專案外的 _unused_backup/&lt;時間&gt;/（不是刪除，可以搬回來）。
///
/// 保留規則：
///   1. 模板場景的所有依賴（材質、貼圖、模型、腳本…）
///   2. ProjectSettings 參照到的資源（URP 設定、XR 設定、Input System…，但不含其他場景）
///   3. AlwaysKeep 清單中的資料夾
///   4. 腳本：被保留的腳本用到的型別所在的腳本（反覆擴展直到不再增加），
///      以及這些腳本所屬 / 引用的 asmdef 整個組件 —— 避免搬走後編譯錯誤
/// </summary>
public static class ExhibitionProjectCleaner
{
    const string BackupRoot = "_unused_backup";

    static readonly string[] AlwaysKeep =
    {
        ExhibitionTemplateBuilder.Root,
        "Assets/ArtworkParticles",
        "Assets/Works",               // 同學透過上傳網頁放入的作品
        "Assets/Settings",
        "Assets/XR",
        "Assets/Oculus",
        "Assets/MetaXR",
        "Assets/Plugins",
        "Assets/Resources",
        "Assets/StreamingAssets",
        "Assets/TextMesh Pro",
        "Assets/InputSystem_Actions.inputactions",
    };

    [MenuItem("Tools/展覽模板/3. 清理與展覽無關的資源（移到專案外備份）", priority = 3)]
    static void CleanMenu()
    {
        var scenePath = ExhibitionTemplateBuilder.ScenePath;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
        {
            EditorUtility.DisplayDialog("清理", "找不到模板場景，請先執行「2. 產生空白展覽模板場景」。", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        EditorUtility.DisplayProgressBar("清理", "分析依賴中…", 0.3f);
        List<string> toMove;
        try { toMove = ComputeUnused(scenePath); }
        finally { EditorUtility.ClearProgressBar(); }

        if (toMove.Count == 0) { EditorUtility.DisplayDialog("清理", "沒有可清理的資源。", "OK"); return; }

        var projectRoot = Directory.GetParent(Application.dataPath).FullName;
        long total = toMove.Sum(p => new FileInfo(p).Length);
        var byFolder = toMove.GroupBy(TopFolder)
            .Select(g => (folder: g.Key, count: g.Count(), mb: g.Sum(p => new FileInfo(p).Length) / 1048576.0))
            .OrderByDescending(x => x.mb).ToList();

        var listPath = Path.Combine(projectRoot, BackupRoot + "_清單.txt");
        var sb = new StringBuilder($"將移出 {toMove.Count} 個檔案，共 {total / 1048576.0:F1} MB\n\n");
        foreach (var f in byFolder) sb.AppendLine($"{f.mb,9:F1} MB  {f.count,5} 個  {f.folder}");
        sb.AppendLine("\n── 完整清單 ──");
        foreach (var p in toMove) sb.AppendLine(p);
        File.WriteAllText(listPath, sb.ToString(), Encoding.UTF8);

        var summary = string.Join("\n", byFolder.Take(15).Select(f => $"{f.mb:F1} MB   {f.folder}"));
        if (!EditorUtility.DisplayDialog("清理與展覽無關的資源",
                $"將把 {toMove.Count} 個檔案（{total / 1048576.0:F0} MB）移到專案外：\n{BackupRoot}/\n\n{summary}" +
                (byFolder.Count > 15 ? "\n…" : "") + $"\n\n完整清單：{BackupRoot}_清單.txt\n確定執行？",
                "移出", "取消"))
            return;

        var dest = Path.Combine(projectRoot, BackupRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        int moved = 0;
        try
        {
            for (int i = 0; i < toMove.Count; i++)
            {
                var p = toMove[i];
                if (i % 50 == 0) EditorUtility.DisplayProgressBar("清理", p, (float)i / toMove.Count);
                var target = Path.Combine(dest, p);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(p, target);
                if (File.Exists(p + ".meta")) File.Move(p + ".meta", target + ".meta");
                moved++;
            }
            RemoveEmptyDirs("Assets");
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(s => File.Exists(s.path)).ToArray();
        AssetDatabase.Refresh();

        Debug.Log($"[清理] 已移出 {moved} 個檔案到 {dest}\n若 Console 出現編譯錯誤，請從該資料夾把對應檔案（連同 .meta）搬回 Assets。");
        EditorUtility.DisplayDialog("清理", $"完成，已移出 {moved} 個檔案。\n備份位置：\n{dest}\n\n確認專案正常後，可自行刪除備份資料夾。", "OK");
    }

    static List<string> ComputeUnused(string scenePath)
    {
        var all = AssetDatabase.GetAllAssetPaths()
            .Where(p => p.StartsWith("Assets/") && File.Exists(p))
            .Distinct().ToList();

        var keep = new HashSet<string>();
        void AddWithDeps(string p)
        {
            if (!keep.Add(p)) return;
            foreach (var d in AssetDatabase.GetDependencies(p, true))
                if (d.StartsWith("Assets/")) keep.Add(d);
        }

        // 1. 模板場景
        AddWithDeps(scenePath);

        // 2. ProjectSettings 參照（略過場景，否則 Build Settings 裡的舊場景會把舊資源全部留下）
        foreach (var f in Directory.GetFiles("ProjectSettings", "*.asset"))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"guid: ([0-9a-f]{32})"))
            {
                var p = AssetDatabase.GUIDToAssetPath(m.Groups[1].Value);
                if (p.StartsWith("Assets/") && !p.EndsWith(".unity")) AddWithDeps(p);
            }

        // 3. 一定保留的資料夾
        foreach (var p in all)
            if (AlwaysKeep.Any(k => p == k || p.StartsWith(k + "/"))) AddWithDeps(p);

        // 4. 腳本 / asmdef 擴展
        ExpandScripts(all, keep);

        return all.Where(p => !keep.Contains(p)).ToList();
    }

    static void ExpandScripts(List<string> all, HashSet<string> keep)
    {
        var scripts = all.Where(p => p.EndsWith(".cs")).ToList();
        // 宣告的型別名稱 + namespace 第一段（處理只 `using Foo;` 沒直接用型別的情況）
        var declared = scripts.ToDictionary(p => p, p =>
        {
            var text = SafeRead(p);
            return Regex.Matches(text, @"\b(?:class|struct|enum|interface|delegate\s+\w+)\s+(\w+)")
                        .Cast<Match>().Select(m => m.Groups[1].Value)
                        .Concat(Regex.Matches(text, @"\bnamespace\s+(\w+)").Cast<Match>().Select(m => m.Groups[1].Value))
                        .ToList();
        });

        var asmdefs = all.Where(p => p.EndsWith(".asmdef")).ToList();
        var asmdefDir = asmdefs.ToDictionary(p => p, p => Path.GetDirectoryName(p).Replace('\\', '/') + "/");
        var asmdefByName = new Dictionary<string, string>();
        foreach (var a in asmdefs)
        {
            var n = Regex.Match(SafeRead(a), "\"name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            if (n.Length > 0) asmdefByName[n] = a;
        }

        // 腳本所屬的 asmdef = 路徑最長（最近）的那個
        string OwnerAsmdef(string script) => asmdefs.Where(a => script.StartsWith(asmdefDir[a]))
                                                    .OrderByDescending(a => asmdefDir[a].Length).FirstOrDefault();

        var tokens = new HashSet<string>();
        void AddTokens(string p) { foreach (Match m in Regex.Matches(SafeRead(p), @"\w+")) tokens.Add(m.Value); }
        foreach (var s in scripts.Where(keep.Contains)) AddTokens(s);

        bool changed = true;
        while (changed)
        {
            changed = false;

            // 被用到型別的腳本
            foreach (var s in scripts)
                if (!keep.Contains(s) && declared[s].Any(tokens.Contains))
                {
                    keep.Add(s); AddTokens(s); changed = true;
                }

            // 保留腳本所屬的 asmdef，以及它引用的 asmdef（連同其所有腳本）
            foreach (var s in scripts.Where(keep.Contains).ToList())
            {
                var a = OwnerAsmdef(s);
                if (a == null || keep.Contains(a)) continue;
                keep.Add(a); changed = true;
                var text = SafeRead(a);
                foreach (Match m in Regex.Matches(text, "\"(?:GUID:)?([^\"]+)\""))
                {
                    var v = m.Groups[1].Value;
                    var refPath = asmdefByName.TryGetValue(v, out var byName) ? byName : AssetDatabase.GUIDToAssetPath(v);
                    if (string.IsNullOrEmpty(refPath) || !refPath.EndsWith(".asmdef") || keep.Contains(refPath)) continue;
                    foreach (var rs in scripts.Where(x => OwnerAsmdef(x) == refPath && keep.Add(x))) AddTokens(rs);
                }
            }
        }
    }

    static string SafeRead(string p) { try { return File.ReadAllText(p); } catch { return ""; } }

    static string TopFolder(string p)
    {
        var parts = p.Split('/');
        return parts.Length > 2 ? parts[0] + "/" + parts[1] : p;
    }

    static void RemoveEmptyDirs(string dir)
    {
        foreach (var d in Directory.GetDirectories(dir))
        {
            if (Path.GetFileName(d).StartsWith(".")) continue;
            RemoveEmptyDirs(d);
            if (AlwaysKeep.Contains(d.Replace('\\', '/'))) continue; // 例如空的 StreamingAssets
            if (Directory.GetFileSystemEntries(d).Length == 0)
            {
                Directory.Delete(d);
                if (File.Exists(d + ".meta")) File.Delete(d + ".meta");
            }
        }
    }
}

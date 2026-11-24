using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 展覽模板工具（選單 Tools/展覽模板）
///   1. 預覽：列出舊作品會對應到哪個空白板位置
///   2. 產生模板：複製展場場景 → 刪除所有舊作品，在原位置放全新空白板（作品A、B…，各自獨立材質）
///      → 三張切換海報改成空白底 + "Group N" → 刪除與展覽無關的場景物件
///   + 新增作品：在選取的 group 加一件空白作品
/// 原始場景不會被修改。專案資源清理請見 ExhibitionProjectCleaner。
/// </summary>
public static class ExhibitionTemplateBuilder
{
    public const string Root = "Assets/ExhibitionTemplate";
    public const string ScenePath = Root + "/ExhibitionTemplate.unity";
    public const string MatDir = Root + "/Materials";
    const string KeyVisual = "主視覺";
    static readonly string[] GroupNames = { "group1", "group2", "group3" };

    // 與展覽無關、模板中要刪掉的場景根物件
    static readonly string[] UnrelatedRootObjects = { "TcpClientExample", "AdbIntentTest" };

    static readonly Color BoardColor = new Color(0.92f, 0.92f, 0.92f, 1f); // 展板：淺灰
    static readonly Color LabelColor = Color.white;                        // 說明牌：白
    static readonly Color PosterColor = Color.white;
    static readonly Color PosterTextColor = new Color(0.15f, 0.15f, 0.15f, 1f);

    class Work
    {
        public string id, key;
        public readonly List<GameObject> boards = new List<GameObject>();
        public readonly List<GameObject> labels = new List<GameObject>();
    }

    class GroupPlan
    {
        public Transform group;
        public readonly List<Work> works = new List<Work>();
        public readonly List<GameObject> removed = new List<GameObject>();
    }

    // ───────────────────────── 選單 ─────────────────────────

    [MenuItem("Tools/展覽模板/1. 預覽轉換計畫（不會修改場景）", priority = 1)]
    static void PreviewMenu()
    {
        var log = new StringBuilder("[展覽模板] 預覽（尚未修改任何東西）\n");
        if (!TryPlan(SceneManager.GetActiveScene(), log, out _, out _)) return;
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("展覽模板", "轉換計畫已輸出到 Console。", "OK");
    }

    [MenuItem("Tools/展覽模板/2. 產生空白展覽模板場景", priority = 2)]
    static void BuildMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var src = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(src.path)) { EditorUtility.DisplayDialog("展覽模板", "請先儲存目前場景。", "OK"); return; }
        if (src.path == ScenePath) { EditorUtility.DisplayDialog("展覽模板", "目前開的就是模板場景，請先打開原始展場場景（CREATIVE.unity）。", "OK"); return; }
        if (!TryPlan(src, new StringBuilder(), out _, out _)) return;

        if (!EditorUtility.DisplayDialog("展覽模板",
                $"將以「{src.path}」為基礎建立新場景：\n{ScenePath}\n\n" +
                "・所有舊作品從場景刪除，原位置改放空白板\n・切換海報改為空白 + Group N\n・刪除 " +
                string.Join("、", UnrelatedRootObjects) + "\n\n原始場景不會被修改。繼續？", "建立", "取消"))
            return;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            if (!EditorUtility.DisplayDialog("展覽模板", "模板場景已存在，要覆蓋嗎？\n（連同 " + MatDir + " 內的材質）", "覆蓋", "取消"))
                return;
            AssetDatabase.DeleteAsset(ScenePath);
        }
        if (AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.DeleteAsset(MatDir);

        EnsureFolder(Root);
        if (!AssetDatabase.CopyAsset(src.path, ScenePath)) { Debug.LogError("[展覽模板] 複製場景失敗"); return; }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var log = new StringBuilder("[展覽模板] 轉換完成\n");
        if (!TryPlan(scene, log, out var plans, out var posts)) return;

        foreach (var p in plans) ApplyGroup(p);
        if (posts != null) ApplyPosters(posts, log);
        RemoveUnrelatedRoots(scene, log);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        UpdateBuildSettings(src.path);
        AssetDatabase.SaveAssets();

        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("展覽模板", "完成！已開啟 " + ScenePath +
            "\n\n下一步（可選）：Tools/展覽模板/3. 清理與展覽無關的資源", "OK");
    }

    [MenuItem("Tools/展覽模板/新增作品到選取的 group", priority = 20)]
    static void AddWorkMenu()
    {
        var group = SelectedGroup();
        if (group == null)
        {
            EditorUtility.DisplayDialog("展覽模板", "請先在 Hierarchy 選取 group1 / group2 / group3（或其中任一物件）。", "OK");
            return;
        }

        // 放在 Scene 視窗中央、面向目前視角
        var sv = SceneView.lastActiveSceneView;
        var pos = sv != null ? sv.pivot : group.position;
        var fwd = sv != null ? Vector3.ProjectOnPlane(sv.camera.transform.forward, Vector3.up) : Vector3.forward;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;

        var parent = CreateWork(group, pos, Quaternion.LookRotation(fwd.normalized));
        Selection.activeGameObject = parent.gameObject;
        if (sv != null) sv.FrameSelected();
        Debug.Log($"[展覽模板] 已在 {group.name} 新增 {parent.name}，材質位於 {MatDir}/{group.name}/");
    }

    /// 在 group 下建立下一個編號的空白作品（展板 + 說明牌 + 專屬材質），回傳作品物件
    public static Transform CreateWork(Transform group, Vector3 worldPos, Quaternion worldRot)
    {
        var used = new HashSet<string>(group.Cast<Transform>().Select(t => t.name));
        int i = 0;
        while (used.Contains("作品" + LetterName(i))) i++;
        var id = "作品" + LetterName(i);

        var parent = new GameObject(id).transform;
        parent.SetParent(group, false);
        parent.SetPositionAndRotation(worldPos, worldRot);

        var folder = MatDir + "/" + group.name;
        CreateBoard(parent, "展板", Vector3.zero, Quaternion.identity, new Vector3(1.5f, 1.5f, 0.002f),
            NewBlankMaterial($"{folder}/{group.name}_{id}_展板.mat", BoardColor, false));
        CreateBoard(parent, "說明牌", new Vector3(1.2f, -0.35f, 0f), Quaternion.identity, new Vector3(0.6f, 0.8f, 0.002f),
            NewBlankMaterial($"{folder}/{group.name}_{id}_說明牌.mat", LabelColor, true));
        AttachInfo(parent);

        Undo.RegisterCreatedObjectUndo(parent.gameObject, "新增作品");
        EditorSceneManager.MarkSceneDirty(group.gameObject.scene);
        return parent;
    }

    /// 掛上 ExhibitWork 並記錄原始展位大小
    public static ExhibitWork AttachInfo(Transform work)
    {
        var info = work.GetComponent<ExhibitWork>();
        if (info == null) info = work.gameObject.AddComponent<ExhibitWork>();
        var board = work.Find("展板");
        var label = work.Find("說明牌");
        if (board != null && info.boardSlotSize == Vector2.zero) info.boardSlotSize = board.localScale;
        if (label != null && info.labelSlotSize == Vector2.zero) info.labelSlotSize = label.localScale;
        return info;
    }

    [MenuItem("Tools/展覽模板/新增作品到選取的 group", true)]
    static bool AddWorkMenuValidate() => SelectedGroup() != null;

    static Transform SelectedGroup()
    {
        for (var t = Selection.activeTransform; t != null; t = t.parent)
            if (GroupNames.Contains(t.name) && t.parent != null && t.parent.name == "exhibition") return t;
        return null;
    }

    // ───────────────────────── 規劃 ─────────────────────────

    static bool TryPlan(Scene scene, StringBuilder log, out List<GroupPlan> plans, out Transform posts)
    {
        plans = new List<GroupPlan>();
        posts = null;
        var exhibition = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "exhibition");
        if (exhibition == null)
        {
            EditorUtility.DisplayDialog("展覽模板", "目前場景找不到根物件「exhibition」。\n請先打開原始展場場景（CREATIVE.unity）。", "OK");
            return false;
        }
        foreach (var gName in GroupNames)
        {
            var g = exhibition.transform.Find(gName);
            if (g == null) { log.AppendLine($"⚠ 找不到 {gName}，略過"); continue; }
            plans.Add(PlanGroup(g, log));
        }
        posts = exhibition.transform.Find("posts");
        if (posts == null) log.AppendLine("⚠ 找不到 exhibition/posts，海報不處理");
        return true;
    }

    static GroupPlan PlanGroup(Transform g, StringBuilder log)
    {
        var plan = new GroupPlan { group = g };
        var arts = new List<GameObject>();
        var labels = new List<GameObject>();

        foreach (Transform c in g)
        {
            if (!IsBoard(c.gameObject)) plan.removed.Add(c.gameObject);
            else if (IsLabel(c.name)) labels.Add(c.gameObject);
            else arts.Add(c.gameObject);
        }

        // 同名（含 "xxx (1)" 這種複製品，例如正反兩面）視為同一件作品
        int letter = 0;
        foreach (var a in arts)
        {
            var key = Key(a.name);
            var w = plan.works.FirstOrDefault(x => x.key == key);
            if (w == null)
            {
                w = new Work { key = key, id = BaseName(a.name) == KeyVisual ? KeyVisual : "作品" + LetterName(letter++) };
                plan.works.Add(w);
            }
            w.boards.Add(a);
        }

        log.AppendLine($"\n■ {g.name}（舊作品全部刪除，原位置改放空白板）");
        foreach (var w in plan.works)
            log.AppendLine($"  {w.id}  ← 取代: {string.Join(" / ", w.boards.Select(b => b.name))}");

        // 說明牌配對：先比名稱（去掉 "ui"），比不到才用距離
        foreach (var l in labels)
        {
            var lk = LabelKey(l.name);
            var cands = lk.Length > 0 ? plan.works.Where(w => w.key.Contains(lk)).ToList() : new List<Work>();
            bool guessed = cands.Count == 0;
            if (guessed) cands = plan.works;
            if (cands.Count == 0) { plan.removed.Add(l); continue; }

            var best = cands.OrderBy(w => w.boards.Min(b => Vector3.Distance(b.transform.position, l.transform.position))).First();
            best.labels.Add(l);
            log.AppendLine($"    說明牌 {l.name} → {best.id}{(guessed ? "   ⚠ 名稱比對不到，依距離推測" : "")}");
        }

        foreach (var r in plan.removed)
            log.AppendLine($"  ✕ 刪除（3D 模型等非展板物件）: {r.name}");
        return plan;
    }

    // ───────────────────────── 套用 ─────────────────────────

    static void ApplyGroup(GroupPlan plan)
    {
        foreach (var r in plan.removed) Object.DestroyImmediate(r);

        var folder = MatDir + "/" + plan.group.name;
        foreach (var w in plan.works)
        {
            var first = w.boards[0].transform;
            var parent = new GameObject(w.id).transform;
            parent.SetParent(plan.group, false);
            parent.localPosition = first.localPosition;
            parent.SetSiblingIndex(first.GetSiblingIndex());

            var boardMat = NewBlankMaterial($"{folder}/{plan.group.name}_{w.id}_展板.mat", BoardColor, false);
            ReplaceWithBlank(w.boards, parent, "展板", boardMat);

            if (w.labels.Count > 0)
            {
                var labelMat = NewBlankMaterial($"{folder}/{plan.group.name}_{w.id}_說明牌.mat", LabelColor, true);
                ReplaceWithBlank(w.labels, parent, "說明牌", labelMat);
            }
            AttachInfo(parent);
        }
    }

    // 舊物件只拿來當「位置 / 大小」參考，建好全新的空白板後就刪除
    static void ReplaceWithBlank(List<GameObject> olds, Transform parent, string baseName, Material mat)
    {
        for (int i = 0; i < olds.Count; i++)
        {
            var o = olds[i].transform; // 與 parent 同屬一個 group，且 parent 無旋轉、縮放 1
            CreateBoard(parent, i == 0 ? baseName : $"{baseName} {i + 1}",
                o.localPosition - parent.localPosition, o.localRotation, o.localScale, mat);
            Object.DestroyImmediate(olds[i]);
        }
    }

    static GameObject CreateBoard(Transform parent, string name, Vector3 localPos, Quaternion localRot, Vector3 localScale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = localScale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static void ApplyPosters(Transform posts, StringBuilder log)
    {
        var folder = MatDir + "/posters";
        log.AppendLine("\n■ 切換海報");
        foreach (Transform post in posts)
        {
            if (!int.TryParse(post.name, out int n)) continue; // 只處理 posts/1, 2, 3
            var renderers = post.GetComponentsInChildren<MeshRenderer>(true)
                                .Where(r => r.GetComponent<TextMeshPro>() == null).ToList();
            if (renderers.Count == 0) continue;

            var mat = NewBlankMaterial($"{folder}/海報_Group{n}.mat", PosterColor, false);
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                r.gameObject.name = i == 0 ? "海報板" : $"海報板 {i + 1}";
            }

            foreach (var old in post.GetComponentsInChildren<TextMeshPro>(true).ToList())
                Object.DestroyImmediate(old.gameObject);

            var b = LocalBounds(post);
            AddPosterText(post, $"Group {n}", b, true);
            AddPosterText(post, $"Group {n}", b, false);
            log.AppendLine($"  posts/{post.name} → 空白 + 「Group {n}」（正反面）");
        }
    }

    // 海報根物件被壓得很扁（scale z ≈ 0.0006），文字用反向縮放抵銷，才不會變形
    static void AddPosterText(Transform post, string text, Bounds lb, bool front)
    {
        var go = new GameObject(front ? "海報文字 (正面)" : "海報文字 (背面)", typeof(RectTransform));
        var t = go.transform;
        t.SetParent(post, false);
        var ls = post.lossyScale;
        t.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
        float eps = 0.003f / Mathf.Abs(ls.z); // 離板面 3mm，避免 z-fighting
        t.localPosition = new Vector3(lb.center.x, lb.center.y, front ? lb.min.z - eps : lb.max.z + eps);
        t.localRotation = front ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.color = PosterTextColor;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 100f;
        float w = lb.size.x * Mathf.Abs(ls.x), h = lb.size.y * Mathf.Abs(ls.y);
        tmp.rectTransform.sizeDelta = new Vector2(w * 0.8f, h * 0.3f);
    }

    static void RemoveUnrelatedRoots(Scene scene, StringBuilder log)
    {
        log.AppendLine("\n■ 刪除與展覽無關的場景物件");
        foreach (var go in scene.GetRootGameObjects())
            if (UnrelatedRootObjects.Contains(go.name))
            {
                log.AppendLine("  ✕ " + go.name);
                Object.DestroyImmediate(go);
            }
    }

    // ───────────────────────── 工具 ─────────────────────────

    static bool IsBoard(GameObject go)
    {
        if (PrefabUtility.IsAnyPrefabInstanceRoot(go)) return false;
        var mf = go.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null || go.GetComponent<MeshRenderer>() == null) return false;
        var m = mf.sharedMesh.name;
        return m == "Cube" || m == "Quad" || m == "Plane";
    }

    static bool IsLabel(string name) => name.Trim().StartsWith("ui", System.StringComparison.OrdinalIgnoreCase);

    static string BaseName(string n) => Regex.Replace(n.Trim(), @"\s*\(\d+\)\s*$", "").Trim();

    static string Key(string n) => Regex.Replace(BaseName(n).ToLowerInvariant(), @"[\s\p{P}\p{S}]", "");

    static string LabelKey(string n)
    {
        var b = BaseName(n);
        if (b.StartsWith("ui", System.StringComparison.OrdinalIgnoreCase)) b = b.Substring(2);
        return Key(b);
    }

    static string LetterName(int i) => i < 26 ? ((char)('A' + i)).ToString() : LetterName(i / 26 - 1) + (char)('A' + i % 26);

    /// 全新的 URP Lit 空白材質（同學只要換 Base Map）
    public static Material NewBlankMaterial(string path, Color color, bool transparent)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", 0f); // 霧面，像印刷輸出
        if (transparent) // 說明牌常用去背 PNG
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("SHADOWCASTER", false);
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath(path));
        return m;
    }

    static Bounds LocalBounds(Transform root)
    {
        bool has = false;
        var b = new Bounds();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = m.MultiplyPoint3x4(corner);
                if (!has) { b = new Bounds(p, Vector3.zero); has = true; }
                else b.Encapsulate(p);
            }
        }
        return b;
    }

    public static string EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return path;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        return path;
    }

    // 把模板場景放到 Build Settings 第一個並啟用；原始場景改為停用（不刪除）
    static void UpdateBuildSettings(string sourcePath)
    {
        var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        foreach (var s in list) if (s.path == sourcePath) s.enabled = false;
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>
/// 「展覽作品上傳」：在 Unity Editor 內啟動一個小型網頁伺服器，
/// 同學用瀏覽器（同一台電腦或同一個區網的手機/筆電）上傳作品，
/// 由 Unity 直接匯入貼圖/影片、套到展板材質、依比例調整展板並存檔。
///
/// 開啟：Tools/展覽模板/作品上傳網頁伺服器
/// 為什麼用 TcpListener 而不是 HttpListener：Windows 上 HttpListener 要對區網開放需要管理員權限。
/// </summary>
[InitializeOnLoad]
public static class ExhibitUploadServer
{
    public const string WorksRoot = "Assets/Works";
    const string WebPage = ExhibitionTemplateBuilder.Root + "/Editor/Web/index.html";
    const long MaxUploadBytes = 1024L * 1024 * 1024; // 1 GB
    static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg" };
    static readonly string[] VideoExt = { ".mp4", ".mov", ".webm" };

    // 跨 domain reload 保留的設定
    public static int Port { get => EditorPrefs.GetInt("ExhibitServer.port", 8787); set => EditorPrefs.SetInt("ExhibitServer.port", value); }
    public static bool AllowLan { get => EditorPrefs.GetBool("ExhibitServer.lan", true); set => EditorPrefs.SetBool("ExhibitServer.lan", value); }
    public static bool AutoSave { get => EditorPrefs.GetBool("ExhibitServer.autosave", true); set => EditorPrefs.SetBool("ExhibitServer.autosave", value); }
    public static string Pin
    {
        get
        {
            var p = EditorPrefs.GetString("ExhibitServer.pin", "");
            if (p.Length == 0) { p = UnityEngine.Random.Range(1000, 10000).ToString(); EditorPrefs.SetString("ExhibitServer.pin", p); }
            return p;
        }
        set { if (value != EditorPrefs.GetString("ExhibitServer.pin", "")) EditorPrefs.SetString("ExhibitServer.pin", value); }
    }

    public static bool Running => listener != null;
    public static readonly List<string> Log = new List<string>();
    public static event Action Changed;

    static TcpListener listener;
    static Thread acceptThread;
    static readonly ConcurrentQueue<Action> mainQueue = new ConcurrentQueue<Action>();
    static int mainThreadId;

    static ExhibitUploadServer()
    {
        mainThreadId = Thread.CurrentThread.ManagedThreadId;
        EditorApplication.update += Pump;
        AssemblyReloadEvents.beforeAssemblyReload += () => Stop(keepRunningFlag: true);
        EditorApplication.quitting += () => Stop();
        if (SessionState.GetBool("ExhibitServer.running", false))
            EditorApplication.delayCall += () => Start(); // 重新編譯後自動恢復
    }

    // ───────────────────────── 啟動 / 停止 ─────────────────────────

    public static void Start()
    {
        if (Running) return;
        try
        {
            listener = new TcpListener(AllowLan ? IPAddress.Any : IPAddress.Loopback, Port);
            listener.Start();
        }
        catch (Exception e)
        {
            listener = null;
            AddLog("❌ 無法啟動（Port 可能被佔用）：" + e.Message);
            return;
        }
        SessionState.SetBool("ExhibitServer.running", true);
        acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "ExhibitUploadServer" };
        acceptThread.Start();
        AddLog($"✅ 伺服器啟動：{string.Join("  ", Urls())}");
    }

    public static void Stop(bool keepRunningFlag = false)
    {
        if (!keepRunningFlag) SessionState.SetBool("ExhibitServer.running", false);
        var l = listener;
        listener = null;
        try { l?.Stop(); } catch { }
        if (l != null && !keepRunningFlag) AddLog("⏹ 伺服器已停止");
    }

    public static IEnumerable<string> Urls()
    {
        yield return $"http://localhost:{Port}/";
        if (!AllowLan) yield break;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var a in ni.GetIPProperties().UnicastAddresses)
                if (a.Address.AddressFamily == AddressFamily.InterNetwork)
                    yield return $"http://{a.Address}:{Port}/";
        }
    }

    // ───────────────────────── HTTP（背景執行緒） ─────────────────────────

    static void AcceptLoop()
    {
        while (listener != null)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); }
            catch { break; } // listener 被停止
            ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
        }
    }

    class Request
    {
        public string method, path;
        public Dictionary<string, string> query = new Dictionary<string, string>();
        public Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Stream body;
        public long contentLength;
        public string Q(string k) => query.TryGetValue(k, out var v) ? v : "";
    }

    static void HandleClient(TcpClient client)
    {
        using (client)
        {
            client.ReceiveTimeout = client.SendTimeout = 120000;
            var stream = client.GetStream();
            int status = 200; string type = "application/json; charset=utf-8"; byte[] body;
            try
            {
                var req = ParseRequest(stream);
                if (req == null) return;
                (status, type, body) = Route(req);
            }
            catch (Exception e)
            {
                status = 500;
                body = Json(new ApiResult { ok = false, error = e.Message });
            }
            var head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: {type}\r\n" +
                       $"Content-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            try
            {
                var hb = Encoding.ASCII.GetBytes(head);
                stream.Write(hb, 0, hb.Length);
                stream.Write(body, 0, body.Length);
            }
            catch { }
        }
    }

    static Request ParseRequest(NetworkStream s)
    {
        var header = new StringBuilder();
        int b, matched = 0;
        while ((b = s.ReadByte()) != -1) // 讀到 \r\n\r\n
        {
            header.Append((char)b);
            matched = (b == '\r' || b == '\n') ? matched + 1 : 0;
            if (matched == 4) break;
            if (header.Length > 32768) return null;
        }
        var lines = header.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
        var first = lines[0].Split(' ');
        if (first.Length < 2) return null;

        var req = new Request { method = first[0], body = s };
        var target = first[1];
        int q = target.IndexOf('?');
        req.path = Uri.UnescapeDataString(q >= 0 ? target.Substring(0, q) : target);
        if (q >= 0)
            foreach (var kv in target.Substring(q + 1).Split('&'))
            {
                var i = kv.IndexOf('=');
                if (i > 0) req.query[Uri.UnescapeDataString(kv.Substring(0, i))] = Uri.UnescapeDataString(kv.Substring(i + 1).Replace('+', ' '));
            }
        foreach (var line in lines.Skip(1))
        {
            var i = line.IndexOf(':');
            if (i > 0) req.headers[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
        }
        if (req.headers.TryGetValue("Content-Length", out var cl)) long.TryParse(cl, out req.contentLength);
        return req;
    }

    static (int, string, byte[]) Route(Request r)
    {
        if (r.method == "GET" && (r.path == "/" || r.path == "/index.html"))
            return (200, "text/html; charset=utf-8", File.ReadAllBytes(WebPage));

        var pin = r.headers.TryGetValue("X-Exhibit-Pin", out var hp) ? hp : r.Q("pin");
        if (r.path.StartsWith("/api/") && pin != Pin)
            return (403, "application/json; charset=utf-8", Json(new ApiResult { ok = false, error = "PIN 碼錯誤" }));

        switch (r.path)
        {
            case "/api/state":
                return Ok(OnMain(BuildState));

            case "/api/file":
                return ServeFile(r.Q("path"));

            case "/api/create":
                return Ok(OnMain(() => CreateWork(r.Q("group"))));

            case "/api/meta":
                var json = ReadSmallBody(r);
                var meta = JsonUtility.FromJson<MetaReq>(json);
                return Ok(OnMain(() => SetMeta(r.Q("group"), r.Q("work"), meta)));

            case "/api/reset":
                return Ok(OnMain(() => ResetWork(r.Q("group"), r.Q("work"))));

            case "/api/upload":
                var ext = Path.GetExtension(r.Q("name")).ToLowerInvariant();
                var slot = r.Q("slot");
                bool isVideo = VideoExt.Contains(ext);
                if (!ImageExt.Contains(ext) && !isVideo) throw new Exception("只支援 PNG / JPG 圖片或 MP4 / MOV / WEBM 影片");
                if (slot == "label" && isVideo) throw new Exception("說明牌只能放圖片");
                if (r.contentLength <= 0 || r.contentLength > MaxUploadBytes) throw new Exception("檔案大小不正確（上限 1GB）");
                var tmp = Path.Combine(Path.GetTempPath(), "exhibit_" + Guid.NewGuid().ToString("N") + ext);
                try { using (var f = File.Create(tmp)) CopyExactly(r.body, f, r.contentLength); } // 在背景執行緒接收，不卡 Unity
                catch { try { File.Delete(tmp); } catch { } throw; }
                return Ok(OnMain(() => ApplyMedia(r.Q("group"), r.Q("work"), slot, tmp, ext,
                                                  r.Q("fit") != "0", r.Q("audio") == "1")));
        }
        return (404, "application/json; charset=utf-8", Json(new ApiResult { ok = false, error = "Not found" }));
    }

    static (int, string, byte[]) Ok(object o) => (200, "application/json; charset=utf-8", Json(o));
    static byte[] Json(object o) => Encoding.UTF8.GetBytes(JsonUtility.ToJson(o));

    static string ReadSmallBody(Request r)
    {
        if (r.contentLength > 1_000_000) throw new Exception("資料太大");
        var ms = new MemoryStream();
        CopyExactly(r.body, ms, r.contentLength);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    static void CopyExactly(Stream src, Stream dst, long count)
    {
        var buf = new byte[81920];
        while (count > 0)
        {
            int n = src.Read(buf, 0, (int)Math.Min(buf.Length, count));
            if (n <= 0) throw new Exception("上傳中斷");
            dst.Write(buf, 0, n);
            count -= n;
        }
    }

    static (int, string, byte[]) ServeFile(string path)
    {
        path = (path ?? "").Replace('\\', '/');
        if (!path.StartsWith(WorksRoot + "/") || path.Contains("..") || !File.Exists(path))
            return (404, "text/plain", Encoding.UTF8.GetBytes("not found"));
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var type = ext == ".png" ? "image/png" : ext == ".mp4" ? "video/mp4" : ext == ".webm" ? "video/webm"
                 : ext == ".mov" ? "video/quicktime" : "image/jpeg";
        return (200, type, File.ReadAllBytes(path));
    }

    // ───────────────────────── 主執行緒派送 ─────────────────────────

    // Unity API 只能在主執行緒呼叫：背景執行緒把工作排進佇列，等 EditorApplication.update 執行完
    static T OnMain<T>(Func<T> f)
    {
        T result = default; Exception error = null;
        using (var done = new ManualResetEventSlim())
        {
            mainQueue.Enqueue(() =>
            {
                try { result = f(); }
                catch (Exception e) { error = e; }
                finally { done.Set(); }
            });
            if (!done.Wait(TimeSpan.FromMinutes(5))) throw new Exception("Unity 沒有回應（是否卡在對話框？）");
        }
        if (error != null) throw error;
        return result;
    }

    static void Pump()
    {
        while (mainQueue.TryDequeue(out var a)) a();
    }

    // ───────────────────────── 場景操作（主執行緒） ─────────────────────────

    [Serializable] public class ApiResult { public bool ok = true; public string error; public string message; }
    [Serializable] public class MetaReq { public string title, artist, description; }
    [Serializable] public class StateDto { public bool ok = true; public string error; public string scene; public List<GroupDto> groups = new List<GroupDto>(); }
    [Serializable] public class GroupDto { public string name; public List<WorkDto> works = new List<WorkDto>(); }
    [Serializable]
    public class WorkDto
    {
        public string id, title, artist, description, boardMedia, boardType, labelMedia;
        public float boardW, boardH, labelW, labelH;
        public bool hasLabel;
    }

    static void EnsureEditable()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new Exception("Unity 正在 Play 模式，請老師先停止 Play 再上傳");
    }

    static Transform Exhibition()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (!s.isLoaded) continue;
            var root = s.GetRootGameObjects().FirstOrDefault(g => g.name == "exhibition");
            if (root != null) return root.transform;
        }
        throw new Exception("Unity 目前沒有打開展覽場景（ExhibitionTemplate.unity）");
    }

    static IEnumerable<Transform> Groups(Transform ex) =>
        ex.Cast<Transform>().Where(t => t.name.StartsWith("group")).OrderBy(t => t.name);

    static IEnumerable<Transform> Works(Transform group) =>
        group.Cast<Transform>().Where(t => t.Find("展板") != null);

    static Transform FindWork(string group, string work)
    {
        var g = Exhibition().Find(group);
        if (g == null) throw new Exception("找不到組別 " + group);
        var w = g.Find(work);
        if (w == null || w.Find("展板") == null) throw new Exception($"找不到 {group}/{work}");
        return w;
    }

    static List<Transform> Boards(Transform w) => w.Cast<Transform>().Where(t => t.name.StartsWith("展板")).ToList();
    static List<Transform> Labels(Transform w) => w.Cast<Transform>().Where(t => t.name.StartsWith("說明牌")).ToList();

    static StateDto BuildState()
    {
        var st = new StateDto();
        var ex = Exhibition();
        st.scene = ex.gameObject.scene.name;
        foreach (var g in Groups(ex))
        {
            var gd = new GroupDto { name = g.name };
            foreach (var w in Works(g))
            {
                var info = ExhibitionTemplateBuilder.AttachInfo(w);
                var board = w.Find("展板");
                var label = w.Find("說明牌");
                gd.works.Add(new WorkDto
                {
                    id = w.name,
                    title = info.title, artist = info.artist, description = info.description,
                    boardMedia = info.boardMedia,
                    boardType = string.IsNullOrEmpty(info.boardMedia) ? "" : VideoExt.Contains(Path.GetExtension(info.boardMedia).ToLowerInvariant()) ? "video" : "image",
                    labelMedia = info.labelMedia,
                    boardW = info.boardSlotSize.x, boardH = info.boardSlotSize.y,
                    hasLabel = label != null,
                    labelW = label != null ? info.labelSlotSize.x : 0, labelH = label != null ? info.labelSlotSize.y : 0,
                });
            }
            st.groups.Add(gd);
        }
        return st;
    }

    static ApiResult CreateWork(string group)
    {
        EnsureEditable();
        var g = Exhibition().Find(group);
        if (g == null) throw new Exception("找不到組別 " + group);

        // 放在最後一件作品的右邊、同一個朝向；老師之後可在 Scene 視窗再微調
        var last = Works(g).LastOrDefault();
        Vector3 pos = g.position; Quaternion rot = g.rotation;
        if (last != null)
        {
            var b = last.Find("展板");
            rot = b.rotation;
            pos = b.position + b.right * (b.lossyScale.x * 0.5f + 1.5f);
        }
        var w = ExhibitionTemplateBuilder.CreateWork(g, pos, rot);
        Save(w);
        AddLog($"＋ {group}/{w.name} 新增（位置請老師在 Scene 視窗確認）");
        return new ApiResult { message = w.name };
    }

    static ApiResult SetMeta(string group, string work, MetaReq m)
    {
        EnsureEditable();
        var w = FindWork(group, work);
        var info = ExhibitionTemplateBuilder.AttachInfo(w);
        Undo.RecordObject(info, "更新作品資訊");
        info.title = m.title?.Trim();
        info.artist = m.artist?.Trim();
        info.description = m.description?.Trim();
        Save(w);
        AddLog($"✎ {group}/{work}：{info.title} — {info.artist}");
        return new ApiResult();
    }

    static ApiResult ApplyMedia(string group, string work, string slot, string tmpFile, string ext, bool fit, bool audio)
    {
        try
        {
            EnsureEditable();
            var w = FindWork(group, work);
            var info = ExhibitionTemplateBuilder.AttachInfo(w);
            bool isBoard = slot != "label";
            var targets = isBoard ? Boards(w) : Labels(w);
            if (targets.Count == 0) throw new Exception("這件作品沒有說明牌");

            // 1. 複製到 Assets/Works/groupN/作品X/，取代舊檔
            var dir = ExhibitionTemplateBuilder.EnsureFolder($"{WorksRoot}/{group}/{work}");
            var baseName = isBoard ? "展板" : "說明牌";
            foreach (var old in Directory.GetFiles(dir, baseName + ".*").Where(f => !f.EndsWith(".meta")))
                AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
            var assetPath = $"{dir}/{baseName}{ext}";
            File.Copy(tmpFile, assetPath, true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            // 2. 套到材質（每件作品各自的材質，不會影響別人）
            var mat = EnsureOwnMaterial(group, work, baseName, targets, !isBoard);
            Undo.RecordObject(mat, "更新作品");
            float aspect;
            bool isVideo = VideoExt.Contains(ext);
            if (isVideo)
            {
                var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(assetPath);
                if (clip == null) throw new Exception("影片匯入失敗（格式可能不支援，建議 H.264 MP4）");
                aspect = clip.height > 0 ? (float)clip.width / clip.height : 16f / 9f;
                mat.SetTexture("_BaseMap", null);
                foreach (var t in targets)
                {
                    var vp = t.GetComponent<VideoPlayer>();
                    if (vp == null) vp = Undo.AddComponent<VideoPlayer>(t.gameObject); // 不能用 ??：Editor 的「假 null」
                    vp.source = VideoSource.VideoClip;
                    vp.clip = clip;
                    vp.renderMode = VideoRenderMode.MaterialOverride;
                    vp.targetMaterialRenderer = t.GetComponent<Renderer>();
                    vp.targetMaterialProperty = "_BaseMap";
                    vp.isLooping = true;
                    vp.playOnAwake = true;
                    // 正反兩面只讓第一面出聲，避免重音
                    vp.audioOutputMode = audio && t == targets[0] ? VideoAudioOutputMode.Direct : VideoAudioOutputMode.None;
                }
            }
            else
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = true;
                imp.alphaIsTransparency = imp.DoesSourceTextureHaveAlpha();
                imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.maxTextureSize = 2048; // Quest 記憶體友善
                imp.SaveAndReimport();
                imp.GetSourceTextureWidthAndHeight(out int tw, out int th);
                aspect = th > 0 ? (float)tw / th : 1f;

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                mat.SetTexture("_BaseMap", tex);
                foreach (var t in targets)
                {
                    var vp = t.GetComponent<VideoPlayer>();
                    if (vp != null) Undo.DestroyObjectImmediate(vp);
                }
            }
            mat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(mat);

            // 3. 依比例調整展板（以原始展位為上限，中心不動）
            if (fit) FitToSlot(targets, isBoard ? info.boardSlotSize : info.labelSlotSize, aspect);

            Undo.RecordObject(info, "更新作品");
            if (isBoard) info.boardMedia = assetPath; else info.labelMedia = assetPath;
            Save(w);
            AddLog($"⬆ {group}/{work} {baseName} ← {Path.GetFileName(assetPath)}{(isVideo ? "（影片）" : "")}");
            return new ApiResult { message = assetPath };
        }
        finally
        {
            try { File.Delete(tmpFile); } catch { }
        }
    }

    // 確保展板用的是這件作品專屬的材質資源（舊場景或手動建立的板可能沒有）
    static Material EnsureOwnMaterial(string group, string work, string baseName, List<Transform> targets, bool transparent)
    {
        var r = targets[0].GetComponent<Renderer>();
        var mat = r.sharedMaterial;
        var path = mat != null ? AssetDatabase.GetAssetPath(mat) : "";
        if (!path.StartsWith(ExhibitionTemplateBuilder.MatDir + "/"))
        {
            mat = ExhibitionTemplateBuilder.NewBlankMaterial(
                $"{ExhibitionTemplateBuilder.MatDir}/{group}/{group}_{work}_{baseName}.mat", Color.white, transparent);
        }
        foreach (var t in targets)
        {
            var tr = t.GetComponent<Renderer>();
            if (tr.sharedMaterial != mat) { Undo.RecordObject(tr, "更新作品"); tr.sharedMaterial = mat; }
        }
        return mat;
    }

    static void FitToSlot(List<Transform> targets, Vector2 slot, float aspect)
    {
        if (slot.x <= 0 || slot.y <= 0 || aspect <= 0) return;
        float w, h;
        if (aspect > slot.x / slot.y) { w = slot.x; h = slot.x / aspect; }
        else { h = slot.y; w = slot.y * aspect; }
        foreach (var t in targets)
        {
            Undo.RecordObject(t, "調整展板比例");
            t.localScale = new Vector3(w, h, t.localScale.z);
        }
    }

    static ApiResult ResetWork(string group, string work)
    {
        EnsureEditable();
        var w = FindWork(group, work);
        var info = ExhibitionTemplateBuilder.AttachInfo(w);
        foreach (var (targets, slot) in new[] { (Boards(w), info.boardSlotSize), (Labels(w), info.labelSlotSize) })
        {
            foreach (var t in targets)
            {
                var vp = t.GetComponent<VideoPlayer>();
                if (vp != null) Undo.DestroyObjectImmediate(vp);
                var mat = t.GetComponent<Renderer>().sharedMaterial;
                if (mat != null && AssetDatabase.GetAssetPath(mat).StartsWith(ExhibitionTemplateBuilder.MatDir + "/"))
                {
                    mat.SetTexture("_BaseMap", null);
                    EditorUtility.SetDirty(mat);
                }
                if (slot != Vector2.zero) { Undo.RecordObject(t, "清空作品"); t.localScale = new Vector3(slot.x, slot.y, t.localScale.z); }
            }
        }
        Undo.RecordObject(info, "清空作品");
        info.title = info.artist = info.description = info.boardMedia = info.labelMedia = "";
        var dir = $"{WorksRoot}/{group}/{work}";
        if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
        Save(w);
        AddLog($"⟲ {group}/{work} 已清空");
        return new ApiResult();
    }

    static void Save(Transform anyInScene)
    {
        EditorSceneManager.MarkSceneDirty(anyInScene.gameObject.scene);
        if (!AutoSave) return;
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(anyInScene.gameObject.scene);
    }

    static void AddLog(string s)
    {
        void Do()
        {
            Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {s}");
            if (Log.Count > 200) Log.RemoveAt(Log.Count - 1);
            Debug.Log("[作品上傳] " + s);
            Changed?.Invoke();
        }
        if (Thread.CurrentThread.ManagedThreadId == mainThreadId) Do(); else mainQueue.Enqueue(Do);
    }
}

/// <summary>Tools/展覽模板/作品上傳網頁伺服器</summary>
public class ExhibitUploadWindow : EditorWindow
{
    Vector2 scroll;

    [MenuItem("Tools/展覽模板/作品上傳網頁伺服器", priority = 40)]
    static void Open() => GetWindow<ExhibitUploadWindow>("作品上傳伺服器").Show();

    void OnEnable() => ExhibitUploadServer.Changed += Repaint;
    void OnDisable() => ExhibitUploadServer.Changed -= Repaint;

    void OnGUI()
    {
        var running = ExhibitUploadServer.Running;
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(running))
        {
            ExhibitUploadServer.Port = EditorGUILayout.IntField("Port", ExhibitUploadServer.Port);
            ExhibitUploadServer.AllowLan = EditorGUILayout.Toggle(new GUIContent("允許區網連線", "讓同學用自己的手機/筆電連進來（第一次會跳出 Windows 防火牆，請允許）"), ExhibitUploadServer.AllowLan);
        }
        ExhibitUploadServer.Pin = EditorGUILayout.TextField(new GUIContent("PIN 碼", "同學打開網頁時要輸入"), ExhibitUploadServer.Pin);
        ExhibitUploadServer.AutoSave = EditorGUILayout.Toggle(new GUIContent("每次上傳後自動存檔", "關閉的話需要手動 Ctrl+S"), ExhibitUploadServer.AutoSave);

        EditorGUILayout.Space();
        if (!running)
        {
            if (GUILayout.Button("▶ 啟動伺服器", GUILayout.Height(32))) ExhibitUploadServer.Start();
        }
        else
        {
            if (GUILayout.Button("⏹ 停止伺服器", GUILayout.Height(32))) ExhibitUploadServer.Stop();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("給同學的網址", EditorStyles.boldLabel);
            foreach (var u in ExhibitUploadServer.Urls())
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.SelectableLabel(u, GUILayout.Height(18));
                    if (GUILayout.Button("開啟", GUILayout.Width(50))) Application.OpenURL(u);
                }
            EditorGUILayout.HelpBox($"PIN：{ExhibitUploadServer.Pin}\n請保持 Unity 開著「ExhibitionTemplate」場景，且不要在 Play 模式。", MessageType.Info);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("紀錄", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var l in ExhibitUploadServer.Log) EditorGUILayout.LabelField(l, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
    }
}

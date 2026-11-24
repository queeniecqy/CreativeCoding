using System.Collections;
using System.Linq;
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class MRUKFloorCenterAligner : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform exhibitionRoot;

    [Header("Effect Mesh")]
    [Tooltip("地板所使用的 EffectMesh（需與 MRUK 的 EffectMesh 系統一致）")]
    [SerializeField] private EffectMesh floorEffectMesh;

    [Header("Rotation")]
    [Tooltip("按住 B 時的旋轉速度（度/秒）")]
    [SerializeField] private float rotationSpeed = 45f;
    [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;

    [Header("Height Adjust")]
    [Tooltip("上下微調速度（米/秒），透過右手搖桿垂直方向控制")]
    [SerializeField] private float heightAdjustSpeed = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool verboseLog = true;

    private bool hasAligned = false;

    private const string SavedHeightKey = "ExhibitionSavedHeight";

    // 內部狀態
    private bool wasRotatingLastFrame = false;
    private bool heightChangedThisFrame = false;
    private bool heightWasAdjusted = false;
    private float initialAlignedY = 0f;

    // 紀錄地面中心（繞此點旋轉 & 傳給 TCP）
    private Vector3 floorCenterPos;

    // 用來傳給 TCP 的腳本參考（獨立腳本，透過尋找取得）
    private TcpClientExample tcpClient;

    // 用於「每隔 10 幀發一次」
    private int frameCounter = 0;
    private const int SendIntervalFrames = 10;

    private void Start()
    {
        if (exhibitionRoot == null)
        {
            Debug.LogError("[FloorAligner] exhibitionRoot 未指定！");
            return;
        }

        if (floorEffectMesh == null)
        {
            Debug.LogError("[FloorAligner] floorEffectMesh 未指定！");
            return;
        }

        // 嘗試在場景中找到 TcpClientExample（獨立腳本）
        tcpClient = FindObjectOfType<TcpClientExample>();
        if (tcpClient == null)
        {
            Debug.LogWarning("[FloorAligner] 場景中找不到 TcpClientExample，將無法把地面中心傳到 TCP。");
        }

        exhibitionRoot.gameObject.SetActive(false);
        StartCoroutine(WaitForFloorMeshAndAlign());
    }

    private IEnumerator WaitForFloorMeshAndAlign()
    {
        // 1. 拿房間
        MRUKRoom room = null;
        if (verboseLog) Debug.Log("[FloorAligner] 等待 MRUK 當前房間可用...");
        while ((room = MRUK.Instance.GetCurrentRoom()) == null)
            yield return null;

        if (room.FloorAnchor == null)
        {
            Debug.LogError("[FloorAligner] 當前房間沒有 FloorAnchor！");
            yield break;
        }

        // 2. 等地板 EffectMesh 準備好
        if (verboseLog) Debug.Log("[FloorAligner] 等待 Floor 的 EffectMesh 準備完成...");
        while (!floorEffectMesh.EffectMeshObjects.ContainsKey(room.FloorAnchor))
            yield return null;

        var wrapper = floorEffectMesh.EffectMeshObjects[room.FloorAnchor];
        var meshGo = wrapper?.effectMeshGO;
        if (!meshGo)
        {
            Debug.LogError("[FloorAligner] 找不到 Floor 的 effectMeshGO！");
            yield break;
        }

        // 3. 計算地板網格中心（世界座標）
        var renderers = meshGo.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogError("[FloorAligner] Floor effectMeshGO 下沒有 Renderer！");
            yield break;
        }

        var aabb = renderers[0].bounds;
        foreach (var r in renderers.Skip(1))
            aabb.Encapsulate(r.bounds);

        floorCenterPos = aabb.center;   // 記錄地板中心
        if (verboseLog) Debug.Log($"[FloorAligner] Floor 網格 AABB 中心: {floorCenterPos}");

        // 4. 把展館放到地板中心（先不決定旋轉）
        AlignPosition(floorCenterPos);

        // 5. 直接用 KeyWall 自動決定展館朝向（不使用保存的 Y）
        TryAutoAlignRotationWithKeyWall(room);

        // 6. 恢復高度偏移（這個仍然保存）
        initialAlignedY = exhibitionRoot.position.y;
        if (PlayerPrefs.HasKey(SavedHeightKey))
        {
            float savedHeightOffset = PlayerPrefs.GetFloat(SavedHeightKey, 0f);
            var p = exhibitionRoot.position;
            p.y = initialAlignedY + savedHeightOffset;
            exhibitionRoot.position = p;
            if (verboseLog) Debug.Log($"[FloorAligner] 已恢復保存的高度偏移: {savedHeightOffset} m");
        }

        hasAligned = true;
        exhibitionRoot.gameObject.SetActive(true);
        Debug.Log("[FloorAligner] 已用地板中心 + 關鍵牆自動對齊位置與朝向。按住 B 微調旋轉；按 A 繞地面中心 Y 軸翻轉 180°；右手搖桿上下微調高度並保存。");

        // 一開始對齊完成後，可以先立即發送一次（如果有 TCP）
        TrySendFloorCenter();
    }

    private void AlignPosition(Vector3 pos)
    {
        var e = exhibitionRoot.rotation.eulerAngles;
        exhibitionRoot.position = pos;
        exhibitionRoot.rotation = Quaternion.Euler(0f, e.y, 0f); // 先保持原 Y
    }

    private void TryAutoAlignRotationWithKeyWall(MRUKRoom room)
    {
        Vector2 wallScale;
        var keyWall = room.GetKeyWall(out wallScale);
        if (keyWall == null)
        {
            if (verboseLog) Debug.LogWarning("[FloorAligner] GetKeyWall 回傳為 null，無法自動對齊朝向。");
            return;
        }

        Transform wallTransform = keyWall.transform;

        // 讓展館面向房間內部：-forward
        Vector3 desiredForward = -wallTransform.forward;
        desiredForward.y = 0f;
        if (desiredForward.sqrMagnitude < 0.0001f)
        {
            if (verboseLog) Debug.LogWarning("[FloorAligner] keyWall forward 太小，放棄自動旋轉。");
            return;
        }

        desiredForward.Normalize();
        exhibitionRoot.rotation = Quaternion.LookRotation(desiredForward, Vector3.up);

        if (verboseLog)
        {
            Debug.Log("[FloorAligner] 已使用 KeyWall 自動設置展館朝向（背對牆，面向房間內）。");
            Debug.Log($"[FloorAligner] KeyWall pos={wallTransform.position}, rot={wallTransform.rotation.eulerAngles}");
            Debug.Log($"[FloorAligner] 展館 forward={exhibitionRoot.forward}, Y={exhibitionRoot.rotation.eulerAngles.y:F2}°");
        }
    }

    private void Update()
    {
        if (!hasAligned || exhibitionRoot == null) return;

        // ======================
        // A. 每隔 10 幀傳一次地面中心座標
        // ======================
        frameCounter++;
        if (frameCounter >= SendIntervalFrames)
        {
            frameCounter = 0;
            TrySendFloorCenter();
        }

        // ======================
        // B. 原本的控制邏輯
        // ======================

        // 1) 按住 B -> 旋轉（僅作臨時微調）
        bool isRotatingNow = OVRInput.Get(OVRInput.Button.Two, controller);
        // if (isRotatingNow)
        // {
        //     exhibitionRoot.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
        // }
        // wasRotatingLastFrame = isRotatingNow;

        // // 2) 按下 A -> 繞地面中心 Y 軸自轉 180 度
        // if (OVRInput.GetDown(OVRInput.Button.One, controller))
        // {
        //     RotateAroundFloorCenter180();
        // }

        // 3) 右手 Primary Thumbstick 上下 -> 高度微調（米/秒），並保存高度偏移
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controller);
        float deadZone = 0.1f;
        heightChangedThisFrame = false;

        if (Mathf.Abs(stick.y) > deadZone)
        {
            var p = exhibitionRoot.position;
            p.y += stick.y * heightAdjustSpeed * Time.deltaTime;
            exhibitionRoot.position = p;
            heightChangedThisFrame = true;
            heightWasAdjusted = true;
        }

        if (!heightChangedThisFrame && heightWasAdjusted)
        {
            float heightOffset = exhibitionRoot.position.y - initialAlignedY;
            PlayerPrefs.SetFloat(SavedHeightKey, heightOffset);
            PlayerPrefs.Save();
            heightWasAdjusted = false;
            if (verboseLog) Debug.Log($"[FloorAligner] 已保存高度偏移: {heightOffset} m");
        }
    }

    /// <summary>
    /// 嘗試把 floorCenterPos 傳給 TcpClientExample
    /// </summary>
    private void TrySendFloorCenter()
    {
        if (tcpClient == null)
        {
            // 還沒找到就再試一次（避免 Start 時機沒對上）
            tcpClient = FindObjectOfType<TcpClientExample>();
            if (tcpClient == null)
            {
                if (verboseLog)
                {
                    Debug.LogWarning("[FloorAligner] 仍然找不到 TcpClientExample，無法發送地面中心。");
                }
                return;
            }
        }

        // tcpClient.SendFloorCenter(floorCenterPos);
    }

    /// <summary>
    /// 按 A 鍵時，讓展館繞 floorCenterPos 的世界 Y 軸旋轉 180 度。
    /// </summary>
    private void RotateAroundFloorCenter180()
    {
        if (!hasAligned) return;

        // 平移到以 floorCenter 為原點的座標系
        Vector3 offset = exhibitionRoot.position - floorCenterPos;

        // 繞 Y 軸旋轉 180 度
        Quaternion rot180 = Quaternion.AngleAxis(180f, Vector3.up);
        offset = rot180 * offset;

        // 平移回世界座標
        exhibitionRoot.position = floorCenterPos + offset;

        // 同時更新自身朝向 Y + 180°
        exhibitionRoot.rotation = Quaternion.Euler(
            0f,
            exhibitionRoot.rotation.eulerAngles.y + 180f,
            0f
        );

        if (verboseLog)
        {
            Debug.Log("[FloorAligner] 已繞地面中心的 Y 軸自轉 180 度。");
        }
    }
}
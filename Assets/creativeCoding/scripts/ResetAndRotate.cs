using UnityEngine;

public class ResetAndRotate : MonoBehaviour
{
    [Header("設置")]
    [Tooltip("請拖入你要繞著旋轉的那個中心物體 (例如 Camera 或場景中心)")]
    public Transform pivotTarget;

    // 用來存儲物體剛開始的位置和角度
    private Vector3 startPosition;
    private Quaternion startRotation;

    void Start()
    {
        // 遊戲開始時，自動記錄當前的狀態作為「原點」
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    /// <summary>
    /// 供 Unselect 事件調用
    /// </summary>
    public void OnUnselect()
    {
        if (pivotTarget == null)
        {
            Debug.LogWarning("請在 Inspector 中拖入 Pivot Target！");
            return;
        }

        // 1. 先強制歸位 (重置到初始位置)
        transform.position = startPosition;
        transform.rotation = startRotation;

        // 2. 繞著目標物體的 Y 軸瞬間旋轉 180 度
        // 注意：RotateAround 會同時改變物體的位置和角度
        transform.RotateAround(pivotTarget.position, Vector3.up, 180f);
    }
}

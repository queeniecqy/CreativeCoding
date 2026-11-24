using UnityEngine;

public class HoverScaler : MonoBehaviour
{
    [Header("設置")]
    [Tooltip("Hover時的放大倍數，例如 1.5 代表 1.5倍")]
    public float scaleMultiplier = 1.5f;
    
    [Tooltip("縮放變化的速度")]
    public float animationSpeed = 10f;

    private Vector3 originalScale;
    private Vector3 targetScale;

    void Start()
    {
        // 記錄物體原本的大小
        originalScale = transform.localScale;
        // 默認目標大小就是原大小
        targetScale = originalScale;
    }

    void Update()
    {
        // 使用 Lerp 讓縮放平滑過渡，而不是瞬間跳變
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * animationSpeed);
    }

    // --- 供外部調用的方法 ---

    /// <summary>
    /// 當手靠近（Hover）時調用
    /// </summary>
    public void OnHoverEnter()
    {
        targetScale = originalScale * scaleMultiplier;
    }

    /// <summary>
    /// 當手離開（Unhover）時調用
    /// </summary>
    public void OnHoverExit()
    {
        targetScale = originalScale;
    }
}

using UnityEngine;
using TMPro;

public class ControllerTextDisplay : MonoBehaviour
{
    [Header("要顯示文字的 TextMeshPro 元件")]
    [SerializeField] private TMP_Text textComponent;

    private void Awake()
    {
        // 如果沒有手動指定，就嘗試在自己身上找
        if (textComponent == null)
        {
            textComponent = GetComponent<TMP_Text>();
        }

        if (textComponent == null)
        {
            Debug.LogError("[ControllerTextDisplay] 找不到 TMP_Text，請在 Inspector 指定或掛在 TextMeshPro 物件上。");
        }
    }

    /// <summary>
    /// 將指定文字顯示在控制器上。
    /// </summary>
    public void SetText(string text)
    {
        if (textComponent == null) return;
        textComponent.text = text;
    }
}
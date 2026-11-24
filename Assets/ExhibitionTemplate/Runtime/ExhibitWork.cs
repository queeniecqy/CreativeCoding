using UnityEngine;

/// <summary>
/// 掛在每件作品（group/作品X）上，記錄作品資訊與原始展位大小。
/// 由「展覽作品上傳」網頁寫入，也可以直接在 Inspector 修改。
/// </summary>
[DisallowMultipleComponent]
public class ExhibitWork : MonoBehaviour
{
    [Header("作品資訊")]
    public string title;
    public string artist;
    [TextArea(3, 8)] public string description;

    [Header("媒體檔（自動填寫）")]
    public string boardMedia;
    public string labelMedia;

    [Header("原始展位大小（依比例縮放時以此為上限）")]
    public Vector2 boardSlotSize;
    public Vector2 labelSlotSize;
}

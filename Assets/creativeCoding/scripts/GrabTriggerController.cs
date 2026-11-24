using UnityEngine;

public class GrabTriggerController: MonoBehaviour
{
    [Header("抓取後要顯示的物體")]
    public GameObject obj1;

    [Header("抓取後要隱藏的物體")]
    public GameObject obj2;
    public GameObject obj3;

    /// <summary>
    /// 當物體被抓取(Select)時調用此方法
    /// </summary>
    public void OnObjectGrabbed()
    {
        // 顯示 Obj1
        if (obj1 != null) obj1.SetActive(true);

        // 隱藏 Obj2 和 Obj3
        if (obj2 != null) obj2.SetActive(false);
        if (obj3 != null) obj3.SetActive(false);

        Debug.Log("物體被抓取：執行切換邏輯");
    }

    /// <summary>
    /// (可選) 當鬆開物體時調用，如果你希望鬆手後恢復原狀
    /// </summary>
    public void OnObjectReleased()
    {
        // 這裡可以寫鬆手後的邏輯，例如：
        // if (obj1 != null) obj1.SetActive(false);
        // if (obj2 != null) obj2.SetActive(true);
        // if (obj3 != null) obj3.SetActive(true);
    }
}

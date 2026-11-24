using UnityEngine;

public class ToggleByGrip_OVR : MonoBehaviour
{
    [Header("拖入要切換 Activate/Deactivate 的物件")]
    public GameObject target;

    [Header("控制器設定")]
    public OVRInput.Controller controller = OVRInput.Controller.RTouch; // 右手
    public OVRInput.Button gripButton = OVRInput.Button.PrimaryHandTrigger; // Grip（手柄握把）

    private bool wasPressed = false;

    void Update()
    {
        if (target == null) return;

        bool isPressed = OVRInput.Get(gripButton, controller);

        // 檢測按下沿（從未按到按下）
        if (isPressed && !wasPressed)
        {
            ToggleTarget();
        }

        wasPressed = isPressed;
    }

    void ToggleTarget()
    {
        bool nextActive = !(target.activeSelf);
        target.SetActive(nextActive);
    }
}

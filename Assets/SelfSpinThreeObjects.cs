using UnityEngine;

public class SelfSpinThreeObjects : MonoBehaviour
{
    [Header("要自轉的物件（拖入三個）")]
    public GameObject obj1;
    public GameObject obj2;
    public GameObject obj3;

    [Header("每秒旋轉角度（度/秒）。5秒一圈 = 72 度/秒")]
    public float degreesPerSecond = 72f;

    void Update()
    {
        float delta = degreesPerSecond * Time.deltaTime;

        RotateIfValid(obj1, delta);
        RotateIfValid(obj2, delta);
        RotateIfValid(obj3, delta);
    }

    private void RotateIfValid(GameObject go, float delta)
    {
        if (go == null) return;
        // 繞自身的局部 Y 軸自轉
        go.transform.Rotate(0f, delta, 0f, Space.Self);
    }
}

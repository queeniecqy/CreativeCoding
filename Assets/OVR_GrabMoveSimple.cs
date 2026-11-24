using UnityEngine;

public class OVR_GrabMoveSimple : MonoBehaviour
{
    [Header("Grab Settings")]
    public OVRInput.Controller controller = OVRInput.Controller.RTouch; // 右手
    public OVRInput.Button grabButton = OVRInput.Button.PrimaryHandTrigger; // Grip
    public Transform handAnchor; // 例如 OVRCameraRig/RightHandAnchor
    public float followPositionLerp = 20f;
    public float followRotationLerp = 20f;
    public float maxGrabDistance = 0.3f; // 手与物体初始距离阈值

    private bool isGrabbed = false;
    private Vector3 grabOffsetPos;
    private Quaternion grabOffsetRot;

    void Update()
    {
        if (handAnchor == null) return;

        bool pressed = OVRInput.Get(grabButton, controller);

        if (!isGrabbed && pressed)
        {
            // 只有当手在物体附近才允许开始抓取
            float dist = Vector3.Distance(handAnchor.position, transform.position);
            if (dist <= maxGrabDistance)
            {
                StartGrab();
            }
        }
        else if (isGrabbed && !pressed)
        {
            EndGrab();
        }

        if (isGrabbed)
        {
            FollowHand();
        }
    }

    void StartGrab()
    {
        isGrabbed = true;
        // 记录手与物体的相对位姿，保持抓起瞬间的相对关系
        grabOffsetPos = Quaternion.Inverse(handAnchor.rotation) * (transform.position - handAnchor.position);
        grabOffsetRot = Quaternion.Inverse(handAnchor.rotation) * transform.rotation;
    }

    void EndGrab()
    {
        isGrabbed = false;
    }

    void FollowHand()
    {
        // 目标位姿 = 手的位姿 * 抓取时的相对位姿
        Vector3 targetPos = handAnchor.position + handAnchor.rotation * grabOffsetPos;
        Quaternion targetRot = handAnchor.rotation * grabOffsetRot;

        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * followPositionLerp);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * followRotationLerp);
    }
}

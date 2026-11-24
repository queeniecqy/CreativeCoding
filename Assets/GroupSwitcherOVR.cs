using UnityEngine;

public class GroupSwitcherOVR : MonoBehaviour
{
    [Header("Groups (only one active at a time)")]
    public GameObject group1;
    public GameObject group2;
    public GameObject group3;

    [Header("OVR Input Settings")]
    public OVRInput.Controller controller = OVRInput.Controller.RTouch; // 右手控制器
    public float pressThreshold = 0.8f;

    private int currentIndex = 0;
    private GameObject[] groups;
    private bool wasPressed = false;

    void Awake()
    {
        groups = new GameObject[] { group1, group2, group3 };
        SetActiveExclusive(0);
    }

    void Update()
    {
        float triggerValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller);
        bool isPressed = triggerValue >= pressThreshold;

        if (isPressed && !wasPressed)
        {
            NextGroup();
        }
        wasPressed = isPressed;
    }

    private void NextGroup()
    {
        currentIndex = (currentIndex + 1) % groups.Length;
        SetActiveExclusive(currentIndex);
    }

    private void SetActiveExclusive(int index)
    {
        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i] != null)
                groups[i].SetActive(i == index);
        }
    }
}

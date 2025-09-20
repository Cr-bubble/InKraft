using UnityEngine;

public class CursorController : MonoBehaviour
{
    [SerializeField] private StylusHandler _stylusHandler;

    [SerializeField] private Transform parent;
    private bool isHolding = false;
    private bool isChanging = false;

    private GameObject selectedObject;

    public void RotateObject(Vector3 worldPosition, Quaternion? rotation = null)
    {
        if (selectedObject == null)
        {
            Debug.LogError("[DrawController] selectedObject 未指定！");
            return;
        }
        if (rotation.HasValue)
            selectedObject.transform.SetPositionAndRotation(worldPosition, rotation.Value);
        else
            selectedObject.transform.position = worldPosition;
    }

    public GameObject findSelectedObject()
    {
        Ray ray = new Ray(_stylusHandler.CurrentState.inkingPose.position, _stylusHandler.CurrentState.inkingPose.forward);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit))
        {
            return hit.collider.gameObject;
        }
        return null;
    }

    public void Update()
    {
        bool front = _stylusHandler.CurrentState.cluster_front_value;
        Vector3 pos = _stylusHandler.CurrentState.inkingPose.position;
        Quaternion rot = _stylusHandler.CurrentState.inkingPose.rotation;
        if (_stylusHandler.CurrentState.cluster_front_value)
        {
            if (!isHolding) // first select
            {
                selectedObject = findSelectedObject();
                isHolding = true;
            }
            else  // is holding
            {
                if (selectedObject != null)
                {
                    RotateObject(pos, rot);
                }
            }
        }
        else
        {
            selectedObject = null;
            isHolding = false;
        }
    }

    public void Start()
    {
        isHolding = false;
    }
}
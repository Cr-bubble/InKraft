using UnityEngine;

public class CursorController : MonoBehaviour
{
    [SerializeField] private StylusHandler _stylusHandler;

    [SerializeField] private Transform parent;
    private bool isHolding = false;
    private bool isChanging = false;

    private Quaternion inkInitRotation;
    private Quaternion objInitRotation;

    private GameObject selectedObject;

    private Rigidbody _selectedRb;
    private bool _hadRb;
    private bool _prevUseGravity, _prevIsKinematic;

    private Rigidbody GetRigidbodyFor(GameObject go)
{
    if (!go) return null;
    return go.GetComponent<Rigidbody>() ?? go.GetComponentInParent<Rigidbody>();
}

    public void RotateObject(Vector3 worldPosition, Quaternion? rotation = null)
    {
        Quaternion deltaRotation = _stylusHandler.CurrentState.inkingPose.rotation * Quaternion.Inverse(inkInitRotation);
        if (selectedObject == null)
        {
            Debug.LogError("[DrawController] selectedObject 未指定！");
            return;
        }
        if (rotation.HasValue){
            selectedObject.transform.SetPositionAndRotation(worldPosition, deltaRotation * objInitRotation);
        }
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

    private void BeginHold(GameObject go, Quaternion stylusRot)
    {
        selectedObject = go;
        inkInitRotation = stylusRot;
        objInitRotation = selectedObject.transform.rotation;

        _selectedRb = GetRigidbodyFor(selectedObject);
        _hadRb = _selectedRb != null;

        if (_hadRb)
        {
            _prevUseGravity   = _selectedRb.useGravity;
            _prevIsKinematic  = _selectedRb.isKinematic;

            _selectedRb.useGravity = false;
            _selectedRb.isKinematic = true;
            _selectedRb.linearVelocity = Vector3.zero;
            _selectedRb.angularVelocity = Vector3.zero;
        }
    }

    private void EndHold()
{
    if (_hadRb && _selectedRb)
    {
        _selectedRb.useGravity = _prevUseGravity;
        _selectedRb.isKinematic = _prevIsKinematic;
    }
    _selectedRb = null;
    _hadRb = false;
    selectedObject = null;
    isHolding = false;
}

    public void Update()
    {
        bool front = _stylusHandler.CurrentState.cluster_front_value;
        Vector3 pos = _stylusHandler.CurrentState.inkingPose.position;
        Quaternion rot = _stylusHandler.CurrentState.inkingPose.rotation;
        if (front) {
            if (!isHolding) {
                var hitObj = findSelectedObject();
                if(!hitObj || hitObj.name == "Ground") return;
                if (hitObj) {
                    isHolding = true;
                    BeginHold(hitObj, rot);
                }
            } else if (selectedObject) {
                RotateObject(pos, rot);
            }
        } else if (isHolding) {
            EndHold();
        }
    }

    public void Start()
    {
        isHolding = false;
    }
}
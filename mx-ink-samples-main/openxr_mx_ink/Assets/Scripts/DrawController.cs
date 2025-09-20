using UnityEngine;

public class DrawController : MonoBehaviour
{
    [SerializeField] private GameObject spherePrefab;
    [SerializeField] private GameObject cubePrefab;

    [SerializeField] private GameObject spherePreviewObject;
    [SerializeField] private GameObject cubePreviewObject;

    [SerializeField] private GameObject currentPrefab;

    [SerializeField] private StylusHandler _stylusHandler;

    [SerializeField] private Transform parent;
    private bool isPlacing = false;
    private bool isChanging = false;

    public GameObject PlacePrefabAt(Vector3 worldPosition, Quaternion? rotation = null)
    {
        if (currentPrefab == null)
        {
            Debug.LogError("[DrawController] Prefab 未指定！");
            return null;
        }

        var rot = rotation ?? Quaternion.identity;

        var go = Instantiate(currentPrefab, worldPosition, rot, parent);
        return go;
    }

    public void OnSpawnRequested(Vector3 worldPosition, Quaternion rotation)
    {
        PlacePrefabAt(worldPosition, rotation);
    }

    public void disablePreview()
    {
        spherePreviewObject.SetActive(false);
        cubePreviewObject.SetActive(false);
    }

    public void updateCurrent(string currentObject)
    {
        disablePreview();
        if (currentObject == "sphere")
        {
            spherePreviewObject.SetActive(true);
            currentPrefab = spherePrefab;
        }
        else if (currentObject == "cube")
        {
            cubePreviewObject.SetActive(true);
            currentPrefab = cubePrefab;
        }
    }

    private void UpdatePreviewPose(Vector3 pos, Quaternion rot)
    {
        spherePreviewObject.transform.SetPositionAndRotation(pos, rot);
        cubePreviewObject.transform.SetPositionAndRotation(pos, rot);
    }

    public void Update()
    {
        bool front = _stylusHandler.CurrentState.cluster_front_value;
        Vector3 pos = _stylusHandler.CurrentState.inkingPose.position;
        Quaternion rot = _stylusHandler.CurrentState.inkingPose.rotation;
        UpdatePreviewPose(pos, rot);
        if (_stylusHandler.CurrentState.cluster_front_value)
        {
            if (!isPlacing)
            {
                OnSpawnRequested(_stylusHandler.CurrentState.inkingPose.position, _stylusHandler.CurrentState.inkingPose.rotation);
                isPlacing = true;
            }
        }
        else
        {
            isPlacing = false;
        }

        if (_stylusHandler.CurrentState.cluster_back_value)
        {
            if (!isChanging)
            {
                isChanging = true;
                if (currentPrefab == spherePrefab)
                    updateCurrent("cube");
                else if (currentPrefab == cubePrefab)
                    updateCurrent("sphere");
            }
        }
        else
        {
            isChanging = false;
        }
    }

    public void Start()
    {
        updateCurrent("sphere");
        isPlacing = false;
        isChanging = false;
    }
}
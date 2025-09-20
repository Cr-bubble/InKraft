using UnityEngine;

public class Controller : MonoBehaviour
{

    [SerializeField] private StylusHandler _stylusHandler;
    [SerializeField] private GameObject systemPanel;

    [SerializeField] private GameObject cursorController;
    [SerializeField] private GameObject eraserController;
    [SerializeField] private GameObject pencilController;
    [SerializeField] private GameObject placeController;

    private bool isPanelOn = false;

    private string mode_cursor = "place"; // place pencil eraser cursor

    private bool isPressingFront = false;
    private bool isPressingBack = false;

    public void disableAllController()
    {
        cursorController.SetActive(false);
        eraserController.SetActive(false);
        pencilController.SetActive(false);
        placeController.SetActive(false);
    }

    public void changeMode(string mode)  // place pencil eraser cursor
    {
        // be called by UI buttons
        mode_cursor = mode;
        disableAllController();
        if (mode == "place" && placeController != null) placeController.SetActive(true);
        else if (mode == "pencil" && pencilController != null) pencilController.SetActive(true);
        else if (mode == "eraser" && eraserController != null) eraserController.SetActive(true);
        else if (mode == "cursor" && cursorController != null)
        {
            Debug.Log("set cursor active");
            cursorController.SetActive(true);
        }
        systemPanel.SetActive(false);
        isPanelOn = false;
    }

    public bool check_btn_front()
    {
        if (_stylusHandler.CurrentState.cluster_front_value)
        {
            if (!isPressingFront)
            {
                isPressingFront = true;
                return true;
            }
            return false;
        }
        else
        {
            isPressingFront = false;
            return false;
        }
    }

    public bool check_btn_back()
    {
        if (_stylusHandler.CurrentState.cluster_back_value)
        {
            if (!isPressingBack)
            {
                isPressingBack = true;
                return true;
            }
            return false;
        }
        else
        {
            isPressingBack = false;
            return false;
        }
    }

    public void Update()
    {
        if (check_btn_back())
        {
            if (isPanelOn) isPanelOn = false;
            else isPanelOn = true;
            systemPanel.SetActive(isPanelOn);
        }
        Debug.Log($"isPanelOn: {isPanelOn}");
    }

    public void Start()
    {
        isPressingBack = false;
        isPressingFront = false;
        systemPanel.SetActive(true);
        disableAllController();
        changeMode("place");
    }
}
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ARUIManager : MonoBehaviour
{
    public Button backButton;

    void Start()
    {
        if (backButton) backButton.onClick.AddListener(() =>
        {
            SceneManager.LoadScene("PreviewScene_new");
        });
    }
}

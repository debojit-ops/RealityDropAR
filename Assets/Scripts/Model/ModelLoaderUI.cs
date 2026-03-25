using UnityEngine;
using System.Collections;

public class ModelLoaderUI : MonoBehaviour
{
    public GameObject spinner;
    public ModelPreviewManager previewManager;

    public void StartLoad(string path)
    {
        if (spinner) spinner.SetActive(true);
        StartCoroutine(LoadAndHide(path));
    }

    private IEnumerator LoadAndHide(string path)
    {
        yield return null;
        yield return previewManager.LoadModel(path).AsCoroutine();
        if (spinner) spinner.SetActive(false);
    }
}

public static class TaskExtensions
{
    public static IEnumerator AsCoroutine(this System.Threading.Tasks.Task task)
    {
        while (!task.IsCompleted) yield return null;
        if (task.IsFaulted) throw task.Exception;
    }
}

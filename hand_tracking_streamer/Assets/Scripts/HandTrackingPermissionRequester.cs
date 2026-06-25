using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class HandTrackingPermissionRequester : MonoBehaviour
{
    private const string HandTrackingPermission = "com.oculus.permission.HAND_TRACKING";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RequestOnStartup()
    {
        var requester = new GameObject(nameof(HandTrackingPermissionRequester));
        DontDestroyOnLoad(requester);
        requester.AddComponent<HandTrackingPermissionRequester>();
    }

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(HandTrackingPermission))
        {
            Permission.RequestUserPermission(HandTrackingPermission);
        }
#endif
    }
}

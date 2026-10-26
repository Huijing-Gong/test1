using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Owns the browser right-click suppression for this Side A scene only.
/// Mouse aim, pointer lock and other worlds remain unchanged.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleWebGLMouseControls : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void SideA_AcquireMouseControls();
    [DllImport("__Internal")] static extern void SideA_ReleaseMouseControls();
#endif
    bool registered;

    void OnEnable()
    {
        if (registered) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        SideA_AcquireMouseControls();
#endif
        registered = true;
    }

    void OnDisable() => Release();
    void OnDestroy() => Release();

    void Release()
    {
        if (!registered) return;
        registered = false;
#if UNITY_WEBGL && !UNITY_EDITOR
        SideA_ReleaseMouseControls();
#endif
    }
}

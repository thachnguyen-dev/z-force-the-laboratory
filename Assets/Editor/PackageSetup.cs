using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Adds the networking packages by name so the Package Manager picks the versions made for this editor:
    /// <c>Unity.exe -batchmode -projectPath src -executeMethod ZombieGame.EditorTools.PackageSetup.AddNetworking</c>
    /// (no -quit: it exits by itself when the request finishes).
    /// </summary>
    public static class PackageSetup
    {
        private static readonly string[] Networking =
        {
            "com.unity.netcode.gameobjects",
            "com.unity.transport",
            "com.unity.services.multiplayer",
        };

        private static AddAndRemoveRequest _request;

        public static void AddNetworking()
        {
            _request = Client.AddAndRemove(Networking);
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (!_request.IsCompleted) return;
            EditorApplication.update -= Poll;
            if (_request.Status == StatusCode.Success)
            {
                foreach (var package in _request.Result)
                    if (System.Array.IndexOf(Networking, package.name) >= 0) Debug.Log($"[ZOMBIE-PACKAGES] {package.name} {package.version}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[ZOMBIE-PACKAGES] failed: {_request.Error?.message}");
                EditorApplication.Exit(1);
            }
        }
    }
}

using System;
using System.Reflection;
using UnityEngine;

public static class StoneBossCameraChecks
{
    // A marked transparent wall must leave both hard and anticipated camera booms
    // clear, while an ordinary wall behind it must still shorten both booms.
    public static string CollisionFiltering()
    {
        var marker = typeof(ThirdPersonCamera).Assembly.GetType("CameraCollisionPassthrough");
        if (marker == null) throw new Exception("FAIL: transparent arena walls have no camera passthrough marker.");
        var root = new GameObject("Camera regression fixture");
        var cameraObject = new GameObject("Camera regression boom");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            root.transform.position = new Vector3(2000, 1000, 2000);
            cameraObject.AddComponent<Camera>();
            var rig = cameraObject.AddComponent<ThirdPersonCamera>();
            typeof(ThirdPersonCamera).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(rig, root.transform);
            wall.transform.SetParent(root.transform, false);
            wall.transform.localPosition = Vector3.back * 2;
            wall.transform.localScale = new Vector3(10, 10, .3f);
            // Keep the obstacle out of the followed target's hierarchy.
            wall.transform.SetParent(null, true);
            Physics.SyncTransforms();
            var hard = typeof(ThirdPersonCamera).GetMethod("TryGetNearestCollision", BindingFlags.Instance | BindingFlags.NonPublic);
            var soft = typeof(ThirdPersonCamera).GetMethod("GetAnticipatedCollisionDistance", BindingFlags.Instance | BindingFlags.NonPublic);
            object[] args = { root.transform.position, Vector3.back, 6f, default(RaycastHit), 0f };
            if (!(bool)hard.Invoke(rig, args) || (float)soft.Invoke(rig, new object[] { root.transform.position, Vector3.back, 6f }) >= 6)
                throw new Exception("FAIL: normal wall must obstruct hard and soft camera sweeps.");
            var tag = wall.AddComponent(marker);
            if ((bool)hard.Invoke(rig, args) || (float)soft.Invoke(rig, new object[] { root.transform.position, Vector3.back, 6f }) < 5.99f)
                throw new Exception("FAIL: marked wall still compresses the camera boom.");
            if (!wall.GetComponent<Collider>().enabled || wall.GetComponent<Collider>().isTrigger)
                throw new Exception("FAIL: camera filtering changed physical blocking.");
            UnityEngine.Object.DestroyImmediate(tag);
            if (!(bool)hard.Invoke(rig, args)) throw new Exception("FAIL: normal obstacle collision was lost.");
            return "PASS: transparent wall ignored by hard/soft sweeps; ordinary walls and solid colliders preserved.";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(wall);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}

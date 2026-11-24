using System.Collections;
using UnityEngine;

namespace VirtueCore.Shared
{
    // Frees materials and meshes left behind by destroyed models and events.
    //
    // Every model part, outline line, hit and track segment gets its own
    // material (and some their own mesh), created at runtime. Destroying the
    // GameObject doesn't destroy those, so each model or event that was
    // replaced left all of them in memory -- a tour alone rebuilds the model
    // on the way in and out. Over a session that adds up, and all of it has
    // to be torn down when the app quits.
    //
    // Resources.UnloadUnusedAssets frees anything nothing references any
    // more. It's run a frame after the objects were destroyed (Destroy only
    // takes effect at the end of the frame), and only once however many
    // callers ask for it in the same frame. It costs a short pause, so it's
    // only requested when a model or event file is replaced, never during
    // tour transitions.
    public static class AssetCleanup
    {
        // Frame the pending unload was requested in (-1: none). A frame
        // number rather than a flag, so a request whose coroutine never ran
        // (its host destroyed by a scene reload) can't block later ones.
        private static int scheduledFrame = -1;

        public static void UnloadUnusedSoon(MonoBehaviour host)
        {
            if (host == null || !host.isActiveAndEnabled)
                return;
            if (scheduledFrame >= 0 && Time.frameCount <= scheduledFrame + 1)
                return;

            scheduledFrame = Time.frameCount;
            host.StartCoroutine(UnloadNextFrame());
        }

        private static IEnumerator UnloadNextFrame()
        {
            yield return null;
            scheduledFrame = -1;
            Resources.UnloadUnusedAssets();
        }
    }
}

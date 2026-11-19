using UnityEngine;

namespace VirtueCore.Shared
{
    // Central render-queue layout shared by model and event geometry.
    //
    // Detector components get per-component queues (count - index) so they
    // draw in a stable, file-defined order. Those queues are clamped into
    // [DetectorQueueMin, DetectorQueueMax], which sits entirely below the
    // built-in Transparent queue (3000) -- so however many components a model
    // has, and whatever index values it uses, a detector part can never reach
    // the event band.
    //
    // Every event object (hits, blocks, clusters, jets, particles, tracks)
    // uses EventQueue, which is above both the detector band and the default
    // Transparent queue (TriLib-loaded FBX detectors, wireframe lines, grid,
    // name tags), so events are always drawn after -- i.e. on top of --
    // detector geometry.
    public static class RenderOrder
    {
        public const int DetectorQueueMin = 1;
        public const int DetectorQueueMax = 2999;
        public const int EventQueue = 3500;

        public static int DetectorQueue(int requested)
        {
            return Mathf.Clamp(requested, DetectorQueueMin, DetectorQueueMax);
        }
    }
}

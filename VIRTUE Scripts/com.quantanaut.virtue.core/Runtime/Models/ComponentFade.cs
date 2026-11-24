using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace VirtueCore.Models
{
    // Fades a model's components (and their outline lines) to new alphas for
    // a tour scene.
    //
    // What keeps tour camera moves smooth:
    //  * Fully transparent parts and lines have their renderers switched OFF.
    //    Unity still draws a material at alpha 0, so a tour model with most
    //    parts hidden used to draw every one of them (and up to 12 outline
    //    lines each) every frame -- a cost that depended on what was in view,
    //    which is why some transitions stuttered and others didn't. They're
    //    switched back on when they fade in.
    //  * Each part's renderer, material and lines are looked up once per
    //    model (the Cache), not every frame.
    //  * Only parts (and lines) whose alpha actually changes are animated;
    //    those are updated every frame, so fades stay smooth.
    //  * The one-off passes over every part (building the cache, reading the
    //    starting alphas) are spread over frames within a small time budget.
    public static class ComponentFade
    {
        public const float DefaultFrameBudgetMs = 1.5f;
        private const int MinPartsPerFrame = 50;

        // Per-model lookups. Rebuilt when the part list is replaced (a new
        // model) or grows.
        public sealed class Cache
        {
            internal List<GameObject> source;
            internal int sourceCount;
            internal Renderer[] renderers;
            internal Material[] materials;
            internal LineRenderer[][] lines;

            public bool Matches(List<GameObject> parts)
            {
                return parts != null && ReferenceEquals(parts, source) && parts.Count == sourceCount;
            }
        }

        // Builds the cache for `parts` (spread over frames), storing it with
        // `store` once complete.
        public static IEnumerator BuildCache(List<GameObject> parts, System.Action<Cache> store,
                                             float frameBudgetMs = DefaultFrameBudgetMs)
        {
            int count = parts.Count;
            Cache cache = new Cache
            {
                source = parts,
                sourceCount = count,
                renderers = new Renderer[count],
                materials = new Material[count],
                lines = new LineRenderer[count][]
            };

            Stopwatch watch = Stopwatch.StartNew();
            int done = 0;
            for (int i = 0; i < count; i++)
            {
                GameObject part = parts[i];
                if (part != null)
                {
                    Renderer renderer = part.GetComponent<Renderer>();
                    cache.renderers[i] = renderer;
                    // .material: the renderer's own instance (made once), so
                    // changing alpha never touches a material shared with
                    // other parts.
                    cache.materials[i] = renderer != null ? renderer.material : null;
                    cache.lines[i] = part.GetComponentsInChildren<LineRenderer>(true);
                }
                else
                {
                    cache.lines[i] = new LineRenderer[0];
                }

                if (++done >= MinPartsPerFrame && watch.Elapsed.TotalMilliseconds > frameBudgetMs)
                {
                    yield return null;
                    watch.Restart();
                    done = 0;
                }
            }

            store(cache);
        }

        // Fades every part i to meshTargets[i] (its material alpha) and
        // lineTargets[i] (its lines' alpha) over `duration` seconds, from
        // whatever alphas the parts have now.
        public static IEnumerator Fade(Cache cache, float[] meshTargets, float[] lineTargets, float duration,
                                       float frameBudgetMs = DefaultFrameBudgetMs)
        {
            int count = cache.sourceCount;
            Stopwatch watch = Stopwatch.StartNew();
            int done = 0;

            // 1. Starting alphas and which parts change. Parts already at an
            //    alpha of 0 that stay there are switched off on the way.
            List<int> changing = new List<int>();
            float[] meshStart = new float[count];
            float[][] lineStart = new float[count][];

            for (int i = 0; i < count; i++)
            {
                bool changes = false;

                Material material = cache.materials[i];
                if (material != null)
                {
                    meshStart[i] = material.color.a;
                    if (!Mathf.Approximately(meshStart[i], meshTargets[i]))
                        changes = true;
                    else if (meshTargets[i] <= 0f)
                        SetEnabled(cache.renderers[i], false);
                }

                LineRenderer[] partLines = cache.lines[i];
                float[] starts = new float[partLines.Length];
                for (int j = 0; j < partLines.Length; j++)
                {
                    LineRenderer line = partLines[j];
                    if (line == null) continue;
                    starts[j] = line.startColor.a;
                    if (!Mathf.Approximately(starts[j], lineTargets[i]))
                        changes = true;
                    else if (lineTargets[i] <= 0f)
                        SetEnabled(line, false);
                }
                lineStart[i] = starts;

                if (changes) changing.Add(i);

                if (++done >= MinPartsPerFrame && watch.Elapsed.TotalMilliseconds > frameBudgetMs)
                {
                    yield return null;
                    watch.Restart();
                    done = 0;
                }
            }

            int changeCount = changing.Count;
            if (changeCount == 0)
                yield break;

            // 2. Anything fading in has to be drawn from the start.
            foreach (int i in changing)
            {
                if (meshTargets[i] > 0f && cache.materials[i] != null)
                    SetEnabled(cache.renderers[i], true);

                if (lineTargets[i] > 0f)
                {
                    foreach (LineRenderer line in cache.lines[i])
                        SetEnabled(line, true);
                }
            }

            // 3. Animate every changing part every frame.
            float fadeStart = Time.time;
            while (true)
            {
                float s = duration > 0f ? Mathf.Clamp01((Time.time - fadeStart) / duration) : 1f;

                foreach (int i in changing)
                    Apply(cache, i, meshStart[i], meshTargets[i], lineStart[i], lineTargets[i], s);

                if (s >= 1f)
                    break;
                yield return null;
            }

            // 4. Faded all the way out: stop drawing them.
            foreach (int i in changing)
            {
                if (meshTargets[i] <= 0f)
                    SetEnabled(cache.renderers[i], false);

                if (lineTargets[i] <= 0f)
                {
                    foreach (LineRenderer line in cache.lines[i])
                        SetEnabled(line, false);
                }
            }
        }

        private static void Apply(Cache cache, int i, float meshStart, float meshTarget,
                                  float[] lineStarts, float lineTarget, float s)
        {
            Material material = cache.materials[i];
            if (material != null && !Mathf.Approximately(meshStart, meshTarget))
            {
                Color c = material.color;
                c.a = Mathf.Lerp(meshStart, meshTarget, s);
                material.color = c;
            }

            LineRenderer[] partLines = cache.lines[i];
            for (int j = 0; j < partLines.Length; j++)
            {
                LineRenderer line = partLines[j];
                if (line == null || Mathf.Approximately(lineStarts[j], lineTarget)) continue;

                float a = Mathf.Lerp(lineStarts[j], lineTarget, s);
                Color sc = line.startColor;
                Color ec = line.endColor;
                sc.a = a;
                ec.a = a;
                line.startColor = sc;
                line.endColor = ec;
            }
        }

        private static void SetEnabled(Renderer renderer, bool enabled)
        {
            if (renderer != null && renderer.enabled != enabled)
                renderer.enabled = enabled;
        }
    }
}

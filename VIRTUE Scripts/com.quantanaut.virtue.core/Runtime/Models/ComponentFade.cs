using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace VirtueCore.Models
{
    // Fades a model's components (and their outline lines) to new alphas for
    // a tour scene, without stalling frames -- which, with thousands of
    // parts, made the tour camera stutter while it was moving.
    //
    //  * Each part's material and lines are looked up once per model (the
    //    Cache), not every frame.
    //  * Only parts whose alpha actually changes are animated.
    //  * Work is capped per frame (frameBudgetMs, always at least
    //    MinPartsPerFrame parts), staggering the rest over the following
    //    frames -- the same idea as clearing event objects 1,000 at a time.
    //    When more parts change than fit in a frame, each one is updated
    //    every few frames instead of every frame; the fade still finishes
    //    in about `duration` and every part ends exactly on its target.
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
                    // .material: the renderer's own instance (made once), so
                    // changing alpha never touches a material shared with
                    // other parts.
                    cache.materials[i] = renderer != null ? renderer.material : null;
                    cache.lines[i] = part.GetComponentsInChildren<LineRenderer>();
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

            // 1. Starting alphas, and which parts actually change.
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
                    changes |= !Mathf.Approximately(meshStart[i], meshTargets[i]);
                }

                LineRenderer[] partLines = cache.lines[i];
                float[] starts = new float[partLines.Length];
                for (int j = 0; j < partLines.Length; j++)
                {
                    if (partLines[j] == null) continue;
                    starts[j] = partLines[j].startColor.a;
                    changes |= !Mathf.Approximately(starts[j], lineTargets[i]);
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

            // 2. Animate, round-robin within the frame budget.
            float fadeStart = Time.time;
            int cursor = 0;
            while (true)
            {
                float s = duration > 0f ? Mathf.Clamp01((Time.time - fadeStart) / duration) : 1f;
                if (s >= 1f)
                    break;

                watch.Restart();
                for (int processed = 0; processed < changeCount; processed++)
                {
                    int i = changing[cursor];
                    Apply(cache, i, meshStart[i], meshTargets[i], lineStart[i], lineTargets[i], s);
                    cursor = (cursor + 1) % changeCount;

                    if (processed + 1 >= MinPartsPerFrame && watch.Elapsed.TotalMilliseconds > frameBudgetMs)
                        break;
                }

                yield return null;
            }

            // 3. Land every changing part exactly on its target.
            watch.Restart();
            done = 0;
            for (int k = 0; k < changeCount; k++)
            {
                int i = changing[k];
                Apply(cache, i, meshStart[i], meshTargets[i], lineStart[i], lineTargets[i], 1f);

                if (++done >= MinPartsPerFrame && watch.Elapsed.TotalMilliseconds > frameBudgetMs)
                {
                    yield return null;
                    watch.Restart();
                    done = 0;
                }
            }
        }

        private static void Apply(Cache cache, int i, float meshStart, float meshTarget,
                                  float[] lineStarts, float lineTarget, float s)
        {
            Material material = cache.materials[i];
            if (material != null)
            {
                Color c = material.color;
                c.a = Mathf.Lerp(meshStart, meshTarget, s);
                material.color = c;
            }

            LineRenderer[] partLines = cache.lines[i];
            for (int j = 0; j < partLines.Length; j++)
            {
                LineRenderer line = partLines[j];
                if (line == null) continue;

                float a = Mathf.Lerp(lineStarts[j], lineTarget, s);
                Color sc = line.startColor;
                Color ec = line.endColor;
                sc.a = a;
                ec.a = a;
                line.startColor = sc;
                line.endColor = ec;
            }
        }
    }
}

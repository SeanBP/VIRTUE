using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtueCore.Events
{
    // Pure geometry/physics generation for event data: every method here
    // takes explicit parameters and returns what it built, rather than
    // reading or mutating a MonoBehaviour's own fields -- the calling
    // script (per-project) owns the actual tracked lists/arrays and does
    // the appending itself, exactly like it already does with the tuples
    // these methods return.
    public static class EventGeometry
    {
        public static Material MakeMaterial(float[] color_rgba)
        {
            Material material = new Material(Shader.Find("Transparent/Diffuse"))
            {
                color = new Color(
                        color_rgba[0],
                        color_rgba[1],
                        color_rgba[2],
                        color_rgba[3]
                    )
            };
            material.SetFloat("_Mode", 3);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            return material;
        }

        public static Mesh CreateConeMesh(float radius, float height)
        {
            Mesh mesh = new Mesh();

            int segments = 20; // Number of segments for the base circle
            int verticesCount = segments + 2; // Tip + base vertices + center of the base
            Vector3[] vertices = new Vector3[verticesCount];
            int[] triangles = new int[segments * 3 * 2]; // Two sets of triangles (side + base)

            // Tip of the cone (vertex at the origin)
            vertices[0] = new Vector3(0, 0, 0);

            // Base circle vertices (at height along the positive Y axis)
            for (int i = 0; i < segments; i++)
            {
                float angle = 2 * Mathf.PI * i / segments;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                vertices[i + 1] = new Vector3(x, height, z); // Move base vertices up by 'height' on the Y axis
            }

            // Center of the base
            vertices[verticesCount - 1] = new Vector3(0, height, 0);

            // Side triangles (connecting the tip to the base)
            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0; // Tip of the cone
                triangles[i * 3 + 1] = (i == segments - 1) ? 1 : i + 2; // Next base vertex (wrap around at the end)
                triangles[i * 3 + 2] = i + 1; // Current base vertex
            }

            // Base triangles (to close the bottom)
            for (int i = 0; i < segments; i++)
            {
                int baseIndex = segments * 3 + i * 3;
                triangles[baseIndex] = verticesCount - 1; // Center of the base
                triangles[baseIndex + 1] = i + 1; // Current base vertex
                triangles[baseIndex + 2] = (i == segments - 1) ? 1 : i + 2; // Next base vertex (wrap around)
            }

            // Assign vertices and triangles to the mesh
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();

            return mesh;
        }

        public static (GameObject particle, Vector3 finalPosition, Vector3 direction) CreateParticle(Particle particleData, float totScale, Func<float[], Material> makeMaterial)
        {
            // Create a particle GameObject
            GameObject particle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            particle.transform.localScale = new Vector3(particleData.size * totScale, particleData.size * totScale, particleData.size * totScale);
            particle.GetComponent<Collider>().enabled = false;
            particle.GetComponent<Renderer>().enabled = false;

            // Set particle material and color
            Material particleMaterial = makeMaterial(particleData.color_rgba);
            particleMaterial.renderQueue = -1;
            particle.GetComponent<MeshRenderer>().sharedMaterial = particleMaterial;

            // Store the final position and direction. ip.x is negated to match
            // the same right-handed-to-Unity-left-handed display convention
            // used everywhere else (hits/blocks/tracks all negate x); the
            // direction formula's -Cos(b)*Sin(a) term already carries the
            // matching asymmetric sign (parallel to how track momentum's px
            // is reconstructed), so only the position needed this fix.
            Vector3 ip = new Vector3(-particleData.ip[0] * totScale, particleData.ip[1] * totScale, particleData.ip[2] * totScale);
            Vector3 direction = new Vector3(
                -Mathf.Cos(particleData.angle_rad[1]) * Mathf.Sin(particleData.angle_rad[0]),
                Mathf.Sin(particleData.angle_rad[1]),
                Mathf.Cos(particleData.angle_rad[1]) * Mathf.Cos(particleData.angle_rad[0])
            ).normalized;

            return (particle, ip, direction);
        }

        public static (GameObject[], float[]) CreateHitObjects(List<Hits> hits, float totScale, Func<float[], Material> makeMaterial)
        {
            // JsonUtility leaves this null (not an empty list) when the JSON
            // simply omits the "hits" key for an event, which is a normal way
            // for an event file to say "no hits this event" -- treat it the
            // same as an empty array instead of throwing.
            hits ??= new List<Hits>();

            int hitSize = hits.Count;
            GameObject[] eventHitObjects = new GameObject[hitSize];
            float[] timeData = new float[hitSize];

            for (int j = 0; j < hitSize; j++)
            {
                Hits JsonHitObject = hits[j];
                timeData[j] = JsonHitObject.time_ns;
                GameObject hitObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hitObject.transform.position = new Vector3(
                    -1f * JsonHitObject.position[0] * totScale,
                    JsonHitObject.position[1] * totScale,
                    JsonHitObject.position[2] * totScale
                );
                hitObject.transform.localScale = new Vector3(
                    JsonHitObject.size * totScale,
                    JsonHitObject.size * totScale,
                    JsonHitObject.size * totScale
                );
                hitObject.GetComponent<Collider>().enabled = false;
                hitObject.GetComponent<Renderer>().enabled = false;

                Material material = makeMaterial(JsonHitObject.color_rgba);

                hitObject.GetComponent<MeshRenderer>().sharedMaterial = material;
                eventHitObjects[j] = hitObject;
            }

            return (eventHitObjects, timeData);
        }

        public static (GameObject[], float[]) CreateBlockObjects(List<Blocks> blocks, float totScale, Func<float[], Material> makeMaterial)
        {
            blocks ??= new List<Blocks>();

            int blockSize = blocks.Count;

            GameObject[] eventBlockObjects = new GameObject[blockSize];
            Blocks JsonBlockObject = new Blocks();
            float[] blockTimeData = new float[blockSize];
            for (int j = 0; j < blockSize; j++)
            {
                JsonBlockObject = blocks[j];

                blockTimeData[j] = JsonBlockObject.time_ns;

                eventBlockObjects[j] = GameObject.CreatePrimitive(PrimitiveType.Cube);
                eventBlockObjects[j].transform.position = new Vector3(-1f * JsonBlockObject.position[0] * totScale, JsonBlockObject.position[1] * totScale, JsonBlockObject.position[2] * totScale);
                eventBlockObjects[j].transform.localScale = new Vector3(JsonBlockObject.size[0] * totScale, JsonBlockObject.size[1] * totScale, JsonBlockObject.size[2] * totScale);
                eventBlockObjects[j].transform.eulerAngles = new Vector3(JsonBlockObject.euler_angles_deg[0], -JsonBlockObject.euler_angles_deg[1], JsonBlockObject.euler_angles_deg[2]);
                eventBlockObjects[j].GetComponent<Collider>().enabled = false;
                eventBlockObjects[j].GetComponent<Renderer>().enabled = false;

                Material material = makeMaterial(JsonBlockObject.color_rgba);

                material.renderQueue = -1;
                eventBlockObjects[j].GetComponent<MeshRenderer>().sharedMaterial = material;
            }
            return (eventBlockObjects, blockTimeData);
        }

        public static (GameObject[], float[]) CreateClusterObjects(List<Clusters> clusters, float totScale, Func<float[], Material> makeMaterial)
        {
            clusters ??= new List<Clusters>();
            int clusterSize = clusters.Count;

            GameObject[] eventClusterObjects = new GameObject[clusterSize];
            Clusters JsonClusterObject = new Clusters();
            float[] clusterTimeData = new float[clusterSize];
            for (int j = 0; j < clusterSize; j++)
            {
                JsonClusterObject = clusters[j];

                clusterTimeData[j] = JsonClusterObject.time_ns;

                // Get the cluster's coordinates and size
                float x = -1f * JsonClusterObject.position[0] * totScale;
                float y = JsonClusterObject.position[1] * totScale;
                float z = JsonClusterObject.position[2] * totScale;
                float granularity = JsonClusterObject.granularity * totScale;
                float length = JsonClusterObject.length * totScale;

                eventClusterObjects[j] = GameObject.CreatePrimitive(PrimitiveType.Cube);

                // Determine the direction vector
                Vector3 direction = new Vector3(x, y, z).normalized;

                // Position the bar so one end is at the designated coordinates (x, y, z)
                Vector3 position = new Vector3(x, y, z) + direction * (length / 2f);
                eventClusterObjects[j].transform.position = position;

                // Scale the cube (make sure its length corresponds to the 'length' value, and its width to 'granularity')
                eventClusterObjects[j].transform.localScale = new Vector3(granularity, granularity, length);

                // Rotate the cube to face away from the origin (point along the direction vector)
                eventClusterObjects[j].transform.rotation = Quaternion.LookRotation(direction);

                eventClusterObjects[j].GetComponent<Collider>().enabled = false;
                eventClusterObjects[j].GetComponent<Renderer>().enabled = false;

                Material material = makeMaterial(JsonClusterObject.color_rgba);

                eventClusterObjects[j].GetComponent<MeshRenderer>().sharedMaterial = material;
            }
            return (eventClusterObjects, clusterTimeData);
        }

        public static (GameObject[], float[]) CreateJetObjects(List<Jets> jets, float totScale, Func<float[], Material> makeMaterial)
        {
            jets ??= new List<Jets>();
            int jetSize = jets.Count;

            GameObject[] eventJetObjects = new GameObject[jetSize];
            Jets JsonJetObject = new Jets();
            float[] jetTimeData = new float[jetSize];
            for (int j = 0; j < jetSize; j++)
            {
                JsonJetObject = jets[j];
                jetTimeData[j] = JsonJetObject.time_ns;

                float x = -1 * JsonJetObject.vertex[0] * totScale;
                float y = JsonJetObject.vertex[1] * totScale;
                float z = JsonJetObject.vertex[2] * totScale;
                float length = JsonJetObject.length * totScale;
                float theta = JsonJetObject.angle_rad[0];
                float phi = JsonJetObject.angle_rad[1];
                float radius = length * Mathf.Tan(JsonJetObject.R_rad / 2f);

                Vector3 direction = new Vector3(
                -1 * Mathf.Sin(theta) * Mathf.Cos(phi),
                Mathf.Sin(theta) * Mathf.Sin(phi),
                Mathf.Cos(theta)
                ).normalized;

                eventJetObjects[j] = new GameObject();
                Mesh coneMesh = CreateConeMesh(radius, length);
                MeshFilter meshFilter = eventJetObjects[j].AddComponent<MeshFilter>();
                meshFilter.mesh = coneMesh;

                MeshRenderer renderer = eventJetObjects[j].AddComponent<MeshRenderer>();

                Material material = makeMaterial(JsonJetObject.color_rgba);

                renderer.material = material;

                eventJetObjects[j].transform.position = new Vector3(x, y, z);
                eventJetObjects[j].transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(90, 0, 0);  // Rotate 90 degrees around the X axis

                eventJetObjects[j].GetComponent<Renderer>().enabled = false;
            }

            return (eventJetObjects, jetTimeData);
        }

        public static (List<GameObject>, List<float>) CreateTrackObjects(
            List<Tracks> tracks,
            float units,
            float scale,
            float[] trackerGeometry,
            float trackSegmentLength,
            float headerBField,
            Vector3 bFieldDirection,
            double eScale)
        {
            tracks ??= new List<Tracks>();
            int trackSize = tracks.Count;

            List<GameObject> eventTracks = new List<GameObject>();
            List<float> eventTrackTimes = new List<float>();

            // Tracker boundaries in detector coordinates (no display scaling)
            float trackerR = trackerGeometry[0] * units;
            float trackerZn = trackerGeometry[1] * units;
            float trackerZp = trackerGeometry[2] * units;

            for (int j = 0; j < trackSize; j++)
            {
                Tracks JsonTrackObject = tracks[j];

                Color color = new Color(
                    JsonTrackObject.color_rgba[0],
                    JsonTrackObject.color_rgba[1],
                    JsonTrackObject.color_rgba[2],
                    JsonTrackObject.color_rgba[3]
                );

                int q = JsonTrackObject.qOverP < 0 ? -1 : (JsonTrackObject.qOverP > 0 ? 1 : 0);

                float p = 1f;
                double cm = 2.998 * Math.Pow(10, 8);
                if (JsonTrackObject.qOverP != 0)
                {
                    p = (float)(eScale / (cm * Math.Abs(JsonTrackObject.qOverP)));
                }

                float theta = (float)JsonTrackObject.angle_rad[0];
                float phi = (float)JsonTrackObject.angle_rad[1];

                float px = -p * Mathf.Sin(theta) * Mathf.Cos(phi);
                float py = p * Mathf.Sin(theta) * Mathf.Sin(phi);
                float pz = p * Mathf.Cos(theta);

                // Vertex in detector coordinates
                float xo = -JsonTrackObject.vertex[0] * units;
                float yo = JsonTrackObject.vertex[1] * units;
                float zo = JsonTrackObject.vertex[2] * units;

                float B = headerBField;

                float startTime = JsonTrackObject.duration_ns[0];
                float endTime = JsonTrackObject.duration_ns[1];

                float c = 0.299792f;  // m/ns

                Vector3 momentum = new Vector3(px, py, pz);
                float P = momentum.magnitude;

                if (q == 0 || B == 0)
                {
                    Vector3 direction = momentum.normalized;

                    float vx = direction.x * c;
                    float vy = direction.y * c;
                    float vz = direction.z * c;

                    for (float t = 0; t <= endTime; t += trackSegmentLength)
                    {
                        Vector3 startPosition = new Vector3(
                            vx * t + xo,
                            vy * t + yo,
                            vz * t + zo
                        );

                        Vector3 endPosition = new Vector3(
                            vx * (t + trackSegmentLength) + xo,
                            vy * (t + trackSegmentLength) + yo,
                            vz * (t + trackSegmentLength) + zo
                        );

                        float posR = Mathf.Sqrt(endPosition.x * endPosition.x + endPosition.y * endPosition.y);

                        if (endPosition.z < trackerZn || endPosition.z > trackerZp || posR > trackerR)
                        {
                            break;
                        }

                        eventTrackTimes.Add(t + trackSegmentLength + startTime);

                        GameObject segment = new GameObject();
                        LineRenderer lineRenderer = segment.AddComponent<LineRenderer>();
                        lineRenderer.positionCount = 2;

                        // Apply display scale only at rendering
                        lineRenderer.SetPosition(0, startPosition * scale);
                        lineRenderer.SetPosition(1, endPosition * scale);

                        lineRenderer.startWidth = 0.04f;
                        lineRenderer.endWidth = 0.04f;
                        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
                        lineRenderer.startColor = color;
                        lineRenderer.endColor = color;

                        segment.GetComponent<Renderer>().enabled = false;
                        eventTracks.Add(segment);
                    }
                }
                else
                {
                    // bFieldDirection is used exactly as given, with no reflection
                    // applied to it. This was verified against a pre-refactor copy
                    // of this code that never had a field-direction concept at all
                    // (it hardcoded B along +Z, unflipped) but produced
                    // known-correct curvature; HMS2VIRTUE.py's own independent
                    // helix_state (used to generate/validate HMS's track data)
                    // likewise passes its B_FIELD_DIRECTION straight through with
                    // no reflection. A version of this method briefly negated Y
                    // and Z here on the theory that B needed pseudovector
                    // treatment under the position/momentum's X-only reflection --
                    // that reasoning was self-consistent in isolation but wrong:
                    // px/py/pz and xo/yo/zo above are already the final
                    // display-space coordinates the field needs to act on
                    // directly, not physics-frame values awaiting their own
                    // separate reflection.
                    Vector3 bFieldEffective = bFieldDirection;

                    // Build a right-handed orthonormal basis {e1, e2, bFieldEffective}
                    // spanning the plane perpendicular to the field (e1 x e2 =
                    // bFieldEffective). For the default bFieldDirection = +Z this
                    // reduces to e1 = -X, e2 = +Y.
                    Vector3 e1 = Vector3.Cross(Vector3.up, bFieldEffective);
                    if (e1.sqrMagnitude < 1e-6f)
                    {
                        e1 = Vector3.Cross(Vector3.right, bFieldEffective);
                    }
                    e1 = e1.normalized;
                    Vector3 e2 = Vector3.Cross(bFieldEffective, e1);

                    float omega = (q * B / P) * c;
                    float vPar = c * Vector3.Dot(momentum, bFieldEffective) / P;
                    float a0 = c * Vector3.Dot(momentum, e1) / P;
                    float b0 = c * Vector3.Dot(momentum, e2) / P;

                    Vector3 vertexPos = new Vector3(xo, yo, zo);

                    Vector3 HelixPosition(float ht)
                    {
                        float e1Coeff = (a0 * Mathf.Sin(omega * ht) + b0 * (1f - Mathf.Cos(omega * ht))) / omega;
                        float e2Coeff = (a0 * (Mathf.Cos(omega * ht) - 1f) + b0 * Mathf.Sin(omega * ht)) / omega;
                        return vertexPos + e1Coeff * e1 + e2Coeff * e2 + vPar * ht * bFieldEffective;
                    }

                    for (float t = 0; t <= endTime; t += trackSegmentLength)
                    {
                        Vector3 startPosition = HelixPosition(t);
                        Vector3 endPosition = HelixPosition(t + trackSegmentLength);

                        float posR = Mathf.Sqrt(endPosition.x * endPosition.x + endPosition.y * endPosition.y);

                        if (endPosition.z < trackerZn || endPosition.z > trackerZp || posR > trackerR)
                        {
                            break;
                        }

                        eventTrackTimes.Add(t + trackSegmentLength + startTime);

                        GameObject segment = new GameObject();
                        LineRenderer lineRenderer = segment.AddComponent<LineRenderer>();
                        lineRenderer.positionCount = 2;

                        lineRenderer.SetPosition(0, startPosition * scale);
                        lineRenderer.SetPosition(1, endPosition * scale);

                        lineRenderer.startWidth = 0.04f;
                        lineRenderer.endWidth = 0.04f;
                        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
                        lineRenderer.startColor = color;
                        lineRenderer.endColor = color;

                        segment.GetComponent<Renderer>().enabled = false;
                        eventTracks.Add(segment);
                    }
                }
            }

            return (eventTracks, eventTrackTimes);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtueCore.Models
{
    public struct NameTagResult
    {
        public GameObject Text;
        public GameObject Line;
        public GameObject Pivot;
    }

    public struct PieceResult
    {
        public GameObject Piece;
        public List<GameObject> Lines;
        public NameTagResult? NameTag;
    }

    public struct ComponentsBuildResult
    {
        public List<GameObject> DetectorParts;
        public List<GameObject> LineObjects;
        public List<GameObject> NameTagObjects;
        public List<GameObject> Pivots;
    }

    // Pure geometry generation for model/detector components: every method
    // takes explicit parameters and returns what it built, rather than
    // reading or mutating a MonoBehaviour's own fields -- the calling
    // script (per-project) owns detectorParts/lineObjects/nameTagObjects/
    // pivots and does the appending itself (BuildComponents does this
    // bookkeeping internally and returns one aggregate result for the
    // common case of building an entire model file's components in one go).
    public static class ModelGeometry
    {
        // Function to normalize angles to [0, 360)
        public static float NormalizeAngle(float angle)
        {
            angle %= 360; // Get the remainder when divided by 360
            if (angle < 0) angle += 360; // Ensure positive angle
            return angle;
        }

        public static PieceResult MakeBlock(string name, float[] position, float[] size, float[] eulerAngle, float[] rgba, int renderQueue, bool isReal, float scale, float lineThickness, bool collidersOn)
        {
            // Create a new GameObject for the prism
            GameObject prism = new GameObject("Detector Piece", typeof(MeshFilter), typeof(MeshRenderer));
            prism.tag = "Detector";

            // Create the mesh
            Mesh mesh = new Mesh();

            // Set up vertices based on size array
            Vector3[] vertices = new Vector3[8]
            {
            new Vector3(-scale*size[0] / 2, -scale*size[1] / 2, -scale*size[2] / 2),
            new Vector3(scale*size[0] / 2, -scale*size[1] / 2, -scale*size[2] / 2),
            new Vector3(scale*size[0] / 2, scale*size[1] / 2, -scale*size[2] / 2),
            new Vector3(-scale*size[0] / 2, scale*size[1] / 2, -scale*size[2] / 2),
            new Vector3(-scale*size[0] / 2, -scale*size[1] / 2, scale*size[2] / 2),
            new Vector3(scale*size[0] / 2, -scale*size[1] / 2, scale*size[2] / 2),
            new Vector3(scale*size[0] / 2, scale*size[1] / 2, scale*size[2] / 2),
            new Vector3(-scale*size[0] / 2, scale*size[1] / 2, scale*size[2] / 2)
            };

            // Define triangles
            int[] triangles = new int[]
            {
            0, 2, 1, 0, 3, 2, // Back face
            4, 5, 6, 4, 6, 7, // Front face
            0, 1, 5, 0, 5, 4, // Bottom face
            2, 3, 7, 2, 7, 6, // Top face
            0, 4, 7, 0, 7, 3, // Left face
            1, 2, 6, 1, 6, 5,  // Right face

            2, 3, 0, 1, 2, 0, // Faces in reverse
            7, 6, 4, 6, 5, 4,
            4, 5, 0, 5, 1, 0,
            6, 7, 2, 7, 3, 2,
            3, 7, 0, 7, 4, 0,
            5, 6, 1, 6, 2, 1
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;

            prism.GetComponent<MeshFilter>().mesh = mesh;
            if (isReal)
            {
                MeshCollider meshCollider = prism.AddComponent<MeshCollider>();
                meshCollider.enabled = collidersOn;
            }

            // Material and color setup
            Material material = new Material(Shader.Find("Transparent/Diffuse"));

            Color color = new Color(rgba[0], rgba[1], rgba[2], rgba[3]);

            material.color = color;
            material.renderQueue = renderQueue;

            prism.GetComponent<MeshRenderer>().sharedMaterial = material;

            // Create lines to outline the edges of the rectangular prism
            List<GameObject> lineObjects = new List<GameObject>();
            GameObject[] lines = new GameObject[12];
            int[,] edges = new int[12, 2]
            {
            {0, 1}, {1, 2}, {2, 3}, {3, 0}, // Back face
            {4, 5}, {5, 6}, {6, 7}, {7, 4}, // Front face
            {0, 4}, {1, 5}, {2, 6}, {3, 7}  // Connecting edges
            };

            Material lineMaterial = new Material(Shader.Find("Sprites/Default"));

            if (isReal)
            {
                for (int i = 0; i < 12; i++)
                {
                    lines[i] = new GameObject("Line");
                    LineRenderer lineRenderer = lines[i].AddComponent<LineRenderer>();
                    lineRenderer.positionCount = 2;
                    lineRenderer.useWorldSpace = false;

                    lineRenderer.startWidth = lineThickness;
                    lineRenderer.endWidth = lineThickness;

                    lineRenderer.SetPosition(0, vertices[edges[i, 0]]);
                    lineRenderer.SetPosition(1, vertices[edges[i, 1]]);

                    lineRenderer.material = lineMaterial;
                    lineRenderer.material.renderQueue = -1;

                    lines[i].transform.parent = prism.transform;
                    lines[i].transform.localPosition = Vector3.zero;
                    lines[i].SetActive(false);
                    lines[i].tag = "Line";
                    lineObjects.Add(lines[i]);
                }
            }

            // Set orientation and position
            prism.transform.eulerAngles = new Vector3(eulerAngle[0], -eulerAngle[1], eulerAngle[2]);
            prism.transform.position = new Vector3(-scale * position[0], scale * position[1], scale * position[2]);

            NameTagResult? nameTag = null;
            if (!String.Equals(name, ""))
            {
                nameTag = CreateNameTag(prism, name, eulerAngle, renderQueue, lineThickness);
            }

            return new PieceResult { Piece = prism, Lines = lineObjects, NameTag = nameTag };
        }

        public static PieceResult MakeSpheroid(string name, float[] position, float[] size, float[] eulerAngle, float[] rgba, int renderQueue, float scale, float lineThickness, bool collidersOn)
        {
            float[] clear = { 0, 0, 0, 0 };

            // --- build hidden supporting block ---
            PieceResult hiddenBlockResult = MakeBlock(name, position, size, eulerAngle, clear, renderQueue, false, scale, lineThickness, collidersOn);
            GameObject hiddenBlock = hiddenBlockResult.Piece;

            // --- build visible spheroid ---
            GameObject spheroid = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            spheroid.tag = "Detector";

            // parent the hidden block under the sphere
            hiddenBlock.transform.parent = spheroid.transform;
            hiddenBlock.SetActive(false);

            // material
            Material material = new Material(Shader.Find("Transparent/Diffuse"));
            material.color = new Color(rgba[0], rgba[1], rgba[2], rgba[3]);
            material.renderQueue = renderQueue;
            spheroid.GetComponent<Renderer>().material = material;

            // wireframe circles
            List<GameObject> lineObjects = new List<GameObject>();
            lineObjects.Add(CreateCircle(spheroid, Vector3.right, lineThickness));
            lineObjects.Add(CreateCircle(spheroid, Vector3.up, lineThickness));
            lineObjects.Add(CreateCircle(spheroid, Vector3.forward, lineThickness));

            // transform
            spheroid.transform.localScale = new Vector3(scale * size[0], scale * size[1], scale * size[2]);
            spheroid.transform.position = new Vector3(-scale * position[0], scale * position[1], scale * position[2]);
            spheroid.transform.eulerAngles = new Vector3(eulerAngle[0], -eulerAngle[1], eulerAngle[2]);

            MeshCollider meshCollider = spheroid.AddComponent<MeshCollider>();
            meshCollider.enabled = collidersOn;
            UnityEngine.Object.Destroy(spheroid.GetComponent<SphereCollider>());

            // *** only the spheroid is the tour-visible "component" (the hidden
            // block is parented under it but never added to DetectorParts) ***
            return new PieceResult { Piece = spheroid, Lines = lineObjects, NameTag = hiddenBlockResult.NameTag };
        }

        public static GameObject CreateCircle(GameObject parent, Vector3 axis, float lineThickness)
        {
            int segments = 64;
            float radius = 0.5f;
            GameObject lineObj = new GameObject("Wireframe Circle");
            LineRenderer lineRenderer = lineObj.AddComponent<LineRenderer>();

            lineRenderer.positionCount = segments + 1;
            lineRenderer.widthMultiplier = lineThickness;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));

            for (int i = 0; i <= segments; i++)
            {
                float angle = i * 2 * Mathf.PI / segments;
                Vector3 point = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
                point = Quaternion.LookRotation(axis) * point;
                lineRenderer.SetPosition(i, point);
            }

            lineObj.transform.parent = parent.transform;
            lineObj.GetComponent<LineRenderer>().useWorldSpace = false;

            lineObj.SetActive(false);
            return lineObj;
        }

        public static PieceResult MakeToroid(string name, int sides, float[] position, float[] rLeft, float[] rRight, float[] length, float offsetIn, float[] eulerAngle, float[] rgba, int renderQueue, float scale, float lineThickness, bool collidersOn)
        {
            List<GameObject> lineObjects = new List<GameObject>();

            float lengthIn = scale * length[0];
            float lengthOut = scale * length[1];
            offsetIn = scale * offsetIn;
            float innerR = scale * rLeft[0];
            float outerR = scale * rLeft[1];
            float innerR2 = scale * rRight[0];
            float outerR2 = scale * rRight[1];
            float rotate = 0f;
            rotate = (float)rotate * (180.0f / Mathf.PI);

            if (sides % 2 == 0)
            {
                rotate = rotate + (360 / (sides * 2)) + 90;
            }
            else
            {
                rotate = rotate + 90;
            }
            Vector3[] vertices = new Vector3[sides * 4];
            int[] triangles = new int[sides * 12 * 4];
            GameObject[] lines = new GameObject[sides * 8];

            int index = 0;
            int lineIndex = 0;
            for (int i = 0; i < sides; i++)
            {
                float angle = (360f / sides) * i + rotate;
                double theta = Math.PI * angle / 180.0;
                vertices[index] = new Vector3(outerR * (float)Math.Cos(theta), outerR * (float)Math.Sin(theta), (-lengthOut / 2));
                index++;
                vertices[index] = new Vector3(innerR * (float)Math.Cos(theta), innerR * (float)Math.Sin(theta), (-lengthIn / 2) + offsetIn);
                index++;
                vertices[index] = new Vector3(outerR2 * (float)Math.Cos(theta), outerR2 * (float)Math.Sin(theta), (lengthOut / 2));
                index++;
                vertices[index] = new Vector3(innerR2 * (float)Math.Cos(theta), innerR2 * (float)Math.Sin(theta), (lengthIn / 2) + offsetIn);
                index++;

                float angle2 = (360f / sides) * (i + 1) + rotate;
                double theta2 = Math.PI * angle2 / 180.0;
                Vector3 start;
                Vector3 end;
                LineRenderer lr = new LineRenderer();
                Material whiteDiffuseMat = new Material(Shader.Find("Sprites/Default"));
                for (float j = (-0.5f); j <= 0.5f; j++)
                {

                    start = new Vector3(outerR * (float)Math.Cos(theta), outerR * (float)Math.Sin(theta), j * lengthOut);
                    end = new Vector3(outerR * (float)Math.Cos(theta2), outerR * (float)Math.Sin(theta2), j * lengthOut);
                    if (j > 0)
                    {
                        start = new Vector3(outerR2 * (float)Math.Cos(theta), outerR2 * (float)Math.Sin(theta), j * lengthOut);
                        end = new Vector3(outerR2 * (float)Math.Cos(theta2), outerR2 * (float)Math.Sin(theta2), j * lengthOut);
                    }
                    lines[lineIndex] = new GameObject();

                    lines[lineIndex].transform.position = start;
                    lines[lineIndex].AddComponent<LineRenderer>();
                    lr = lines[lineIndex].GetComponent<LineRenderer>();
                    lr.material = whiteDiffuseMat;
                    lr.material.renderQueue = -1;
                    lr.SetWidth(lineThickness, lineThickness);
                    lr.SetPosition(0, start);
                    lr.SetPosition(1, end);
                    lineIndex++;
                    if ((innerR != 0 && j < 0) ^ (innerR2 != 0 && j > 0))
                    {
                        start = new Vector3(innerR * (float)Math.Cos(theta), innerR * (float)Math.Sin(theta), (j * lengthIn) + offsetIn);
                        end = new Vector3(innerR * (float)Math.Cos(theta2), innerR * (float)Math.Sin(theta2), (j * lengthIn) + offsetIn);
                        if (j > 0)
                        {
                            start = new Vector3(innerR2 * (float)Math.Cos(theta), innerR2 * (float)Math.Sin(theta), (j * lengthIn) + offsetIn);
                            end = new Vector3(innerR2 * (float)Math.Cos(theta2), innerR2 * (float)Math.Sin(theta2), (j * lengthIn) + offsetIn);
                        }
                        lines[lineIndex] = new GameObject();

                        lines[lineIndex].transform.position = start;
                        lines[lineIndex].AddComponent<LineRenderer>();
                        lr = lines[lineIndex].GetComponent<LineRenderer>();
                        lr.material = whiteDiffuseMat;
                        lr.material.renderQueue = -1;
                        lr.SetWidth(lineThickness, lineThickness);
                        lr.SetPosition(0, start);
                        lr.SetPosition(1, end);
                        lineIndex++;
                        if (sides <= 1)
                        {
                            start = new Vector3(outerR * (float)Math.Cos(theta), outerR * (float)Math.Sin(theta), j * lengthOut);
                            end = new Vector3(innerR * (float)Math.Cos(theta), innerR * (float)Math.Sin(theta), (j * lengthIn) + offsetIn);
                            if (j > 0)
                            {
                                start = new Vector3(outerR2 * (float)Math.Cos(theta), outerR2 * (float)Math.Sin(theta), j * lengthOut);
                                end = new Vector3(innerR2 * (float)Math.Cos(theta), innerR2 * (float)Math.Sin(theta), (j * lengthIn) + offsetIn);
                            }
                            lines[lineIndex] = new GameObject();

                            lines[lineIndex].transform.position = start;
                            lines[lineIndex].AddComponent<LineRenderer>();
                            lr = lines[lineIndex].GetComponent<LineRenderer>();
                            lr.material = whiteDiffuseMat;
                            lr.material.renderQueue = -1;
                            lr.SetWidth(lineThickness, lineThickness);
                            lr.SetPosition(0, start);
                            lr.SetPosition(1, end);
                            lineIndex++;
                        }

                    }

                }
                if (sides <= 0)
                {
                    start = new Vector3(outerR * (float)Math.Cos(theta), outerR * (float)Math.Sin(theta), (-lengthOut / 2));
                    end = new Vector3(outerR2 * (float)Math.Cos(theta), outerR2 * (float)Math.Sin(theta), (lengthOut / 2));

                    lines[lineIndex] = new GameObject();

                    lines[lineIndex].transform.position = start;
                    lines[lineIndex].AddComponent<LineRenderer>();
                    lr = lines[lineIndex].GetComponent<LineRenderer>();
                    lr.material = whiteDiffuseMat;
                    lr.material.renderQueue = 100;
                    lr.SetWidth(lineThickness, lineThickness);
                    lr.SetPosition(0, start);
                    lr.SetPosition(1, end);
                    lineIndex++;

                    if (innerR > 0f && innerR2 > 0f)
                    {
                        start = new Vector3(innerR * (float)Math.Cos(theta), innerR * (float)Math.Sin(theta), (-lengthIn / 2) + offsetIn);
                        end = new Vector3(innerR2 * (float)Math.Cos(theta), innerR2 * (float)Math.Sin(theta), (lengthIn / 2) + offsetIn);
                        lines[lineIndex] = new GameObject();

                        // NOTE: preserved from the original -- this specific line
                        // object is added here AND again in the final collection
                        // loop below (only reachable when sides <= 0).
                        lineObjects.Add(lines[lineIndex]);
                        lines[lineIndex].transform.position = start;
                        lines[lineIndex].AddComponent<LineRenderer>();
                        lr = lines[lineIndex].GetComponent<LineRenderer>();
                        lr.material = whiteDiffuseMat;
                        lr.material.renderQueue = -1;
                        lr.SetWidth(lineThickness, lineThickness);
                        lr.SetPosition(0, start);
                        lr.SetPosition(1, end);
                        lineIndex++;
                    }
                }

            }
            index = 0;

            //front and back faces
            for (int i = 0; i < sides; i++)
            {
                for (int j = 0; j <= 2; j = j + 2)
                {
                    //side 1
                    triangles[index] = (i * 4) + j;
                    index++;
                    triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                    index++;
                    triangles[index] = ((i * 4) + 1) + j;
                    index++;
                    triangles[index] = ((i * 4) + 1) + j;
                    index++;
                    triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                    index++;
                    triangles[index] = ((((i + 1) * 4) % (sides * 4)) + 1) + j;
                    index++;

                    //side 2
                    triangles[index] = ((((i + 1) * 4) % (sides * 4)) + 1) + j;
                    index++;
                    triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                    index++;
                    triangles[index] = ((i * 4) + 1) + j;
                    index++;
                    triangles[index] = ((i * 4) + 1) + j;
                    index++;
                    triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                    index++;
                    triangles[index] = (i * 4) + j;
                    index++;
                }
            }

            //inner and outer faces
            for (int i = 0; i < sides; i++)
            {
                for (int j = 0; j <= 1; j++)
                {
                    //outer pointing faces
                    triangles[index] = (i * 4) + j;
                    index++;
                    triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                    index++;
                    triangles[index] = (i * 4) + 2 + j;
                    index++;
                    triangles[index] = (i * 4) + 2 + j;
                    index++;
                    triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                    index++;
                    triangles[index] = (((i * 4) + 6) % (sides * 4)) + j;
                    index++;

                    //inner pointing faces
                    triangles[index] = (((i * 4) + 6) % (sides * 4)) + j;
                    index++;
                    triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                    index++;
                    triangles[index] = (i * 4) + 2 + j;
                    index++;
                    triangles[index] = (i * 4) + 2 + j;
                    index++;
                    triangles[index] = (((i + 1) * 4) % (sides * 4)) + j;
                    index++;
                    triangles[index] = (i * 4) + j;
                    index++;
                }
            }

            Mesh mesh = new Mesh();

            mesh.vertices = vertices;
            mesh.triangles = triangles;

            GameObject gameObject = new GameObject("Detector Piece", typeof(MeshFilter), typeof(MeshRenderer));
            gameObject.tag = "Detector";
            gameObject.GetComponent<MeshFilter>().mesh = mesh;
            MeshCollider meshCollider = gameObject.AddComponent<MeshCollider>();
            meshCollider.enabled = collidersOn;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != null)
                {

                    lines[i].transform.parent = gameObject.transform;
                    lines[i].GetComponent<LineRenderer>().useWorldSpace = false;
                    lines[i].transform.position = new Vector3(0, 0, 0);
                    lineObjects.Add(lines[i]);
                    lines[i].SetActive(false);
                }
            }

            Material material = new Material(Shader.Find("Transparent/Diffuse"));
            Color color = new Color(rgba[0], rgba[1], rgba[2], rgba[3]);

            material.color = color;
            material.renderQueue = renderQueue;

            gameObject.GetComponent<MeshRenderer>().sharedMaterial = material;

            gameObject.transform.eulerAngles = new Vector3(eulerAngle[0], -eulerAngle[1], eulerAngle[2]);

            gameObject.transform.position = new Vector3(-scale * position[0], scale * position[1], scale * position[2]);

            NameTagResult? nameTag = null;
            if (!String.Equals(name, ""))
            {
                nameTag = CreateNameTag(gameObject, name, eulerAngle, renderQueue, lineThickness);
            }

            return new PieceResult { Piece = gameObject, Lines = lineObjects, NameTag = nameTag };
        }

        public static NameTagResult CreateNameTag(GameObject gameObject, string name, float[] rot, int renderQueue, float lineThickness)
        {
            Vector3[] vertices = gameObject.GetComponent<MeshFilter>().mesh.vertices;
            // Create a new GameObject for the text
            GameObject textObject = new GameObject("NameTagText");
            TextMesh textMesh = textObject.AddComponent<TextMesh>();

            // Set text properties
            textMesh.text = name;
            textMesh.fontSize = 48; // Smaller font size
            textMesh.characterSize = 0.1f; // Smaller character size for better resolution
            textMesh.alignment = TextAlignment.Center; // Centered text

            // Calculate the upper-right corner of the mesh bounds
            Bounds bounds = gameObject.GetComponent<MeshFilter>().mesh.bounds;
            Vector3 upperRight = new Vector3(bounds.max.x, bounds.max.y, bounds.max.z);

            if (renderQueue % 2 == 0)
            {
                upperRight = new Vector3(bounds.max.x, bounds.min.y, bounds.max.z);
            }

            // Position the text near the upper-right corner of the mesh
            Vector3 offset = new Vector3(0.2f, 0.2f, 0.0f); // Adjust as needed for spacing
            textObject.transform.position = gameObject.transform.TransformPoint(upperRight + offset);

            if (renderQueue % 2 == 0)
            {
                textObject.transform.position = gameObject.transform.TransformPoint(upperRight - offset);
            }

            // Optionally scale the text if needed
            textObject.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f); // Adjust scale for better sizing

            // Create a new GameObject for the line
            GameObject lineObject = new GameObject("Name Tag Line");
            LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();

            // Set line material and appearance
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startWidth = lineThickness;
            lineRenderer.endWidth = lineThickness;
            lineRenderer.positionCount = 2;

            // Position of the bottom of the text
            Bounds textBounds = textObject.GetComponent<Renderer>().bounds;
            // Main logic for determining textBottomCenter
            Vector3 textBottomCenter = textBounds.center - new Vector3(0, textBounds.extents.y, 0);

            // Normalize rotation angles
            float normalizedX = NormalizeAngle(rot[0]);
            float normalizedY = NormalizeAngle(rot[1]);
            float normalizedZ = NormalizeAngle(rot[2]);

            bool isAngleBetween90And270 = (normalizedX > 90 && normalizedX < 270) ||
                                           (normalizedY > 90 && normalizedY < 270) ||
                                           (normalizedZ > 90 && normalizedZ < 270);

            if (isAngleBetween90And270 && renderQueue % 2 == 1)
            {
                textBottomCenter = textBounds.center + new Vector3(0, textBounds.extents.y, 0);
            }
            else if (isAngleBetween90And270 && renderQueue % 2 == 0)
            {
                textBottomCenter = textBounds.center - new Vector3(0, textBounds.extents.y, 0);
            }
            else if (renderQueue % 2 == 0)
            {
                textBottomCenter = textBounds.center + new Vector3(0, textBounds.extents.y, 0);
            }

            // Initialize the nearest vertex and minimum distance
            Vector3 nearestVertex = Vector3.zero;
            float minDistance = Mathf.Infinity;

            // Find the nearest vertex to the text's bottom center
            foreach (Vector3 vertex in vertices)
            {
                // Convert local vertex position to world space
                Vector3 worldVertex = gameObject.transform.TransformPoint(vertex);

                // Calculate the distance between the vertex and the text's bottom center
                float distance = Vector3.Distance(textBottomCenter, worldVertex);

                // If this vertex is closer than the previous, update nearest vertex
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestVertex = worldVertex;
                }
            }

            // Assign positions for the line
            lineRenderer.SetPosition(0, textBottomCenter); // Start at the bottom of the text
            lineRenderer.SetPosition(1, nearestVertex); // End at the nearest vertex of the mesh

            lineObject.transform.parent = gameObject.transform;
            lineObject.GetComponent<LineRenderer>().useWorldSpace = false;
            lineObject.transform.position = Vector3.zero;
            lineObject.SetActive(false);

            // Create a parent GameObject for the pivot
            GameObject pivotObject = new GameObject("Text Pivot");
            pivotObject.transform.position = textBottomCenter; // Set the pivot position
            textObject.transform.SetParent(pivotObject.transform); // Make text a child of the pivot
            pivotObject.transform.parent = gameObject.transform;

            // Set the pivot's position to the bottom center
            pivotObject.transform.position = textBottomCenter;

            textObject.SetActive(false);
            textObject.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);

            return new NameTagResult { Text = textObject, Line = lineObject, Pivot = pivotObject };
        }

        // Bulk convenience wrapper: builds every component in sortedComponents
        // (already ordered by header.index, -1 treated as "last") and returns
        // one aggregate result. This is the buildSimModel dispatch loop moved
        // here verbatim -- JSON parsing, version checking, and the resulting
        // jsonRoot/detectorText/detectorPartAlphas bookkeeping stay in each
        // project's own script.
        public static ComponentsBuildResult BuildComponents(List<Components> sortedComponents, float scale, float lineThickness, bool collidersOn)
        {
            var result = new ComponentsBuildResult
            {
                DetectorParts = new List<GameObject>(),
                LineObjects = new List<GameObject>(),
                NameTagObjects = new List<GameObject>(),
                Pivots = new List<GameObject>()
            };

            int detCount = 0;

            foreach (var data in sortedComponents)
            {
                string name = data.name;
                int index = (data.index == -1) ? detCount : data.index;

                float[] position = data.position;
                float[] eulerAngle = data.euler_angles_deg;
                float[] rgba = data.color_rgba;

                string typeLower = data.type.ToLowerInvariant();

                if (typeLower.Contains("t"))
                {
                    int sides = data.sides;

                    float[] rLeft = data.radii.left;
                    float[] rRight = data.radii.right;

                    if (rLeft[0] == -1)
                        rLeft = rRight;
                    else if (rRight[0] == -1)
                        rRight = rLeft;

                    float[] length = data.length;

                    if (length[0] == -1)
                        length[0] = length[1];
                    else if (length[1] == -1)
                        length[1] = length[0];

                    PieceResult piece = MakeToroid(
                        name,
                        sides,
                        position,
                        rLeft,
                        rRight,
                        length,
                        data.inner_offset,
                        eulerAngle,
                        rgba,
                        sortedComponents.Count - index,
                        scale, lineThickness, collidersOn);

                    AppendPiece(result, piece);
                    detCount++;
                }
                else if (typeLower.Contains("b"))
                {
                    PieceResult piece = MakeBlock(
                        name,
                        position,
                        data.size,
                        eulerAngle,
                        rgba,
                        sortedComponents.Count - index,
                        true,
                        scale, lineThickness, collidersOn);

                    AppendPiece(result, piece);
                    detCount++;
                }
                else if (typeLower.Contains("s"))
                {
                    PieceResult piece = MakeSpheroid(
                        name,
                        position,
                        data.size,
                        eulerAngle,
                        rgba,
                        sortedComponents.Count - index,
                        scale, lineThickness, collidersOn);

                    AppendPiece(result, piece);
                    detCount++;
                }
            }

            return result;
        }

        private static void AppendPiece(ComponentsBuildResult result, PieceResult piece)
        {
            result.DetectorParts.Add(piece.Piece);
            result.LineObjects.AddRange(piece.Lines);
            if (piece.NameTag.HasValue)
            {
                result.NameTagObjects.Add(piece.NameTag.Value.Text);
                result.NameTagObjects.Add(piece.NameTag.Value.Line);
                result.Pivots.Add(piece.NameTag.Value.Pivot);
            }
        }
    }
}

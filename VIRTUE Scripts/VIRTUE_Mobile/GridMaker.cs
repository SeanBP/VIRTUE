using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static System.Net.Mime.MediaTypeNames;

#pragma warning disable 0618

public class GridMaker : MonoBehaviour
{
    public Transform lookAhead;
    public UnityEngine.UI.Text OmText;
    public UnityEngine.UI.Text FmText;
    public UnityEngine.UI.Text lineRenderText;

    private List<GameObject> oneMeterGrid;
    private List<GameObject> fiveMeterGrid;

    private bool isOneMeterGridActive = false;
    private bool isFiveMeterGridActive = false;
    private bool toggleGrid = true;

    private float renderDistance = 0f;

    void Start()
    {
        // Create both grids and set initial states
        oneMeterGrid = CreateGrid(1f, 5);
        fiveMeterGrid = CreateGrid(5f, 15);

        SetGridActive(oneMeterGrid, false);
        SetGridActive(fiveMeterGrid, false);
    }

    public void DeactivateGrids()
    {
        SetGridActive(oneMeterGrid, false);
        SetGridActive(fiveMeterGrid, false);
        isOneMeterGridActive = false;
        isFiveMeterGridActive = false;
    }

    void Update()
    {
        if (toggleGrid)
        {
            // Render the grid lines based on render distance
            if (isOneMeterGridActive) UpdateGridVisibility(oneMeterGrid);
            if (isFiveMeterGridActive) UpdateGridVisibility(fiveMeterGrid);
        }
    }

    public void RenderAll()
    {
        toggleGrid = !toggleGrid;
        lineRenderText.text = toggleGrid ? "Show Full Grid" : "Show Local Grid";

        if (!toggleGrid & isOneMeterGridActive)
        {
            SetGridVisibility(oneMeterGrid, true); 
        }
        if (!toggleGrid & isFiveMeterGridActive)
        {

            SetGridVisibility(fiveMeterGrid, true); 

        }

    }


    public void OneMGrid()
    {
        if (isOneMeterGridActive)
        {
            SetGridActive(oneMeterGrid, false);
            OmText.text = "Show 1m Grid";
        }
        else
        {
            SetGridActive(fiveMeterGrid, false);
            SetGridActive(oneMeterGrid, true);
            renderDistance = 1f;
            OmText.text = "Hide 1m Grid";
            FmText.text = "Show 5m Grid";
        }
    }

    public void FiveMGrid()
    {
        if (isFiveMeterGridActive)
        {
            SetGridActive(fiveMeterGrid, false);
            FmText.text = "Show 5m Grid";
        }
        else
        {
            SetGridActive(oneMeterGrid, false);
            SetGridActive(fiveMeterGrid, true);
            renderDistance = 5f;
            FmText.text = "Hide 5m Grid";
            OmText.text = "Show 1m Grid";
        }
    }

    private List<GameObject> CreateGrid(float spacing, int length)
    {
        List<GameObject> grid = new List<GameObject>();

        float thinLine = 0.01f;
        float thickLine = 0.04f;

        Material whiteDiffuseMat = new Material(Shader.Find("Sprites/Default"));

        for (float i = -length; i <= length; i += spacing)
        {
            for (float j = -length; j <= length; j += spacing)
            {
                grid.Add(CreateLine(new Vector3(j, -length, i), new Vector3(j, length, i), thinLine, thickLine, whiteDiffuseMat));
                grid.Add(CreateLine(new Vector3(-length, j, i), new Vector3(length, j, i), thinLine, thickLine, whiteDiffuseMat));
                grid.Add(CreateLine(new Vector3(j, i, -length), new Vector3(j, i, length), thinLine, thickLine, whiteDiffuseMat));
            }
        }

        return grid;
    }

    private GameObject CreateLine(Vector3 start, Vector3 end, float thinLine, float thickLine, Material material)
    {
        GameObject line = new GameObject("GridLine");

        LineRenderer lr = line.AddComponent<LineRenderer>();
        lr.material = material;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);

        float thickness = (Mathf.Abs(start.x) % 5 == 0 && Mathf.Abs(start.z) % 5 == 0) ? thickLine : thinLine;
        lr.startWidth = thickness;
        lr.endWidth = thickness;

        lr.startColor = Color.white;
        lr.endColor = Color.white;
        lr.useWorldSpace = true;

        return line;
    }

    private void SetGridActive(List<GameObject> grid, bool isActive)
    {
        if (grid == oneMeterGrid) isOneMeterGridActive = isActive;
        if (grid == fiveMeterGrid) isFiveMeterGridActive = isActive;

        if (!toggleGrid & isActive)
        {
            SetGridVisibility(grid, isActive);
        }
        if (!isActive)
        {
            SetGridVisibility(grid, isActive);
        }

    }

    private void SetGridVisibility(List<GameObject> grid, bool isVisible)
    {
        foreach (GameObject line in grid)
        {
            line.GetComponent<LineRenderer>().enabled = isVisible;
        }
    }

    private void UpdateGridVisibility(List<GameObject> grid)
    {
        foreach (GameObject line in grid)
        {
            LineRenderer lr = line.GetComponent<LineRenderer>();
            Vector3 startPoint = lr.GetPosition(0);
            Vector3 endPoint = lr.GetPosition(1);

            bool withinDistance = false;

            if (startPoint.x == -endPoint.x && endPoint.x != 0)
            {
                withinDistance = Math.Abs(lookAhead.position.y - startPoint.y) <= renderDistance &&
                                 Math.Abs(lookAhead.position.z - startPoint.z) <= renderDistance;
            }
            else if (startPoint.y == -endPoint.y && endPoint.y != 0)
            {
                withinDistance = Math.Abs(lookAhead.position.x - startPoint.x) <= renderDistance &&
                                 Math.Abs(lookAhead.position.z - startPoint.z) <= renderDistance;
            }
            else if (startPoint.z == -endPoint.z && endPoint.z != 0)
            {
                withinDistance = Math.Abs(lookAhead.position.y - startPoint.y) <= renderDistance &&
                                 Math.Abs(lookAhead.position.x - startPoint.x) <= renderDistance;
            }

            lr.enabled = withinDistance;
        }
    }
}

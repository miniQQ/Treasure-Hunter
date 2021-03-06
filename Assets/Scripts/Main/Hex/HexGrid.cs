﻿using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Photon.Pun;

public class HexGrid : MonoBehaviour
{
    PhotonView photonView;

    public int cellCountX = 20, cellCountZ = 15;
    int chunkCountX, chunkCountZ;
    public int border = 7;

    public HexCell cellPrefab;
    public Text cellLabelPrefab;
    public HexGridChunk chunkPrefab;
    public HexUnit serverPrefab;
    public HexUnit clientPrefab;
    public HexItem itemPrefab;

    HexCell[] cells;
    HexGridChunk[] chunks;
    List<HexUnit> units = new List<HexUnit>();
    List<HexItem> items = new List<HexItem>();

    HexCellShaderData cellShaderData;

    public HexMesh mesh;

    public Texture2D noiseSource;

    public int seed;

    HexCellPriorityQueue searchFrontier;
    int searchFrontierPhase;

    HexCell currentPathFrom, currentPathTo;
    public bool HasPath
    {
        get
        {
            return currentPathExists;
        }
    }
    bool currentPathExists;

    void Awake()
    {
        HexMetrics.noiseSource = noiseSource;
        HexUnit.serverPrefab = serverPrefab;
        HexUnit.clientPrefab = clientPrefab;

        HexItem.itemPrefab = itemPrefab;

        cellShaderData = gameObject.AddComponent<HexCellShaderData>();
        cellShaderData.Grid = this;

        photonView = PhotonView.Get(this);
    }

    void onEnable()
    {
        if (!HexMetrics.noiseSource)
        {
            HexMetrics.noiseSource = noiseSource;
            HexUnit.serverPrefab = serverPrefab;
            HexUnit.clientPrefab = clientPrefab;

            HexItem.itemPrefab = itemPrefab;

            ResetVisibility();
        }
    }

    public bool CreateMap(int x, int z)
    {
        if (x <= 0 || x % HexMetrics.chunkSizeX != 0 || z <= 0 || z % HexMetrics.chunkSizeZ != 0)
        {
            Debug.LogError("Unsupported map size.");
            return false;
        }

        ClearPath();
        ClearUnits();
        if (chunks != null)
        {
            for (int i = 0; i < chunks.Length; i++)
            {
                Destroy(chunks[i].gameObject);
            }
        }

        cellCountX = x;
        cellCountZ = z;
        chunkCountX = cellCountX / HexMetrics.chunkSizeX;
        chunkCountZ = cellCountZ / HexMetrics.chunkSizeZ;

        cellShaderData.Initialize(cellCountX, cellCountZ);
        CreateChunks();
        CreateCells();
        CreateMeshColliders();

        return true;
    }

    void CreateChunks()
    {
        chunks = new HexGridChunk[chunkCountX * chunkCountZ];

        for (int z = 0, i = 0; z < chunkCountZ; z++)
        {
            for (int x = 0; x < chunkCountX; x++)
            {
                HexGridChunk chunk = chunks[i++] = Instantiate(chunkPrefab);
                chunk.transform.SetParent(transform);
            }
        }
    }

    void CreateCells()
    {
        cells = new HexCell[cellCountZ * cellCountX];
        for (int z = 0, i = 0; z < cellCountZ; z++)
        {
            for (int x = 0; x < cellCountX; x++)
            {
                CreateCell(x, z, i++);
            }
        }
    }

    void CreateCell(int x, int z, int i)
    {
        Vector3 position;
        position.x = (x + z * 0.5f - z / 2) * (HexMetrics.innerRadius * 2f);
        position.y = 0f;
        position.z = z * (HexMetrics.outerRadius * 1.5f);

        HexCell cell = cells[i] = Instantiate<HexCell>(cellPrefab);
        cell.transform.localPosition = position;
        cell.coordinates = HexCoordinates.FromOffsetCorrdinates(x, z);
        cell.Index = i;
        cell.ShaderData = cellShaderData;

        cell.Explorable = x > border && z > border && x < cellCountX - border && z < cellCountZ - border;

        if (x > 0)
        {
            cell.SetNeighbor(HexDirection.W, cells[i - 1]);
        }

        if (z > 0)
        {
            if ((z & 1) == 0) // even rows
            {
                cell.SetNeighbor(HexDirection.SE, cells[i - cellCountX]);
                if (x > 0)
                {
                    cell.SetNeighbor(HexDirection.SW, cells[i - cellCountX - 1]);
                }
            }
            else // odd rows
            {
                cell.SetNeighbor(HexDirection.SW, cells[i - cellCountX]);
                if (x < cellCountX - 1)
                {
                    cell.SetNeighbor(HexDirection.SE, cells[i - cellCountX + 1]);
                }
            }
        }

        Text label = Instantiate<Text>(cellLabelPrefab);
        label.rectTransform.anchoredPosition = new Vector2(position.x, position.z);
        cell.uiRect = label.rectTransform;

        cell.Elevation = 0;

        AddCellToChunk(x, z, cell);
    }

    void AddCellToChunk(int x, int z, HexCell cell)
    {
        int chunkX = x / HexMetrics.chunkSizeX;
        int chunkZ = z / HexMetrics.chunkSizeZ;
        HexGridChunk chunk = chunks[chunkX + chunkZ * chunkCountX];

        int localX = x - chunkX * HexMetrics.chunkSizeX;
        int localZ = z - chunkZ * HexMetrics.chunkSizeZ;
        chunk.AddCell(localX + localZ * HexMetrics.chunkSizeX, cell);
    }

    public void CreateMeshColliders()
    {
        mesh.Clear();

        for (int i = 0; i < cells.Length; i++)
        {
            HexCell cell = cells[i];
            for (int j = 0; j < 6; j++)
            {
                mesh.AddTriangle(cell.Position, cell.Position + HexMetrics.corners[j] * 0.95f, cell.Position + HexMetrics.corners[j + 1] * 0.95f);
            }
        }

        mesh.Apply();
    }

    public HexCell GetCell(Vector3 position)
    {
        position = transform.InverseTransformPoint(position);
        HexCoordinates coordinates = HexCoordinates.FromPosition(position);
        int index = coordinates.X + coordinates.Z * cellCountX + coordinates.Z / 2;
        return cells[index];
    }

    public HexCell GetCell(HexCoordinates coordinates)
    {
        int z = coordinates.Z;
        int x = coordinates.X + z / 2;
        if (z < 0 || z >= cellCountZ || x < 0 || x >= cellCountX)
        {
            return null;
        }
        return cells[x + z * cellCountX];
    }

    public HexCell GetCell(Ray ray)
    {
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit))
        {
            return GetCell(hit.point);
        }
        return null;
    }

    public HexCell GetCell(int xOffset, int zOffset)
    {
        return cells[xOffset + zOffset * cellCountX];
    }

    public HexCell GetCell(int cellIndex)
    {
        return cells[cellIndex];
    }

    public void FindPath(HexCell fromCell, HexCell toCell, HexUnit unit, bool checkIsExplored = true)
    {
        ClearPath();
        currentPathFrom = fromCell;
        currentPathTo = toCell;
        currentPathExists = Search(fromCell, toCell, unit, checkIsExplored);
        if (checkIsExplored)
        {
            ShowPath(unit.Speed);
        }
    }

    bool Search(HexCell fromCell, HexCell toCell, HexUnit unit, bool limitSearch)
    {
        if (fromCell == toCell)
        {
            return false;
        }

        int speed = unit.Speed;

        searchFrontierPhase += 2;
        if (searchFrontier == null)
        {
            searchFrontier = new HexCellPriorityQueue();
        }
        else
        {
            searchFrontier.Clear();
        }

        // A* search
        fromCell.SearchPhase = searchFrontierPhase;
        fromCell.Distance = 0;
        searchFrontier.Enqueue(fromCell);
        while (searchFrontier.Count > 0)
        {
            HexCell current = searchFrontier.Dequeue();
            current.SearchPhase += 1;

            if (current == toCell)
            {
                return true;
            }

            int currentTurn = (current.Distance - 1) / speed;

            for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
            {
                HexCell neighbor = current.GetNeighbor(d);
                // skip null or already calculated neighbor
                if (neighbor == null || neighbor.SearchPhase > searchFrontierPhase)
                {
                    continue;
                }

                // skip unreachable cells
                if (!unit.IsValidDestination(neighbor, limitSearch))
                {
                    continue;
                }

                // calculate movement cost
                int moveCost = unit.GetMoveCost(current, neighbor, d);
                if (moveCost < 0)
                {
                    continue;
                }

                // update distance
                int distance = current.Distance + moveCost;
                if (limitSearch && distance > speed)
                {
                    continue;
                }

                int turn = (distance - 1) / speed;
                if (turn > currentTurn)
                {
                    distance = turn * speed + moveCost;
                }

                if (neighbor.SearchPhase < searchFrontierPhase)
                {
                    neighbor.SearchPhase = searchFrontierPhase;
                    neighbor.Distance = distance;
                    neighbor.PathFrom = current;
                    neighbor.SearchHeuristic = neighbor.coordinates.DistanceTo(toCell.coordinates);
                    searchFrontier.Enqueue(neighbor);
                }
                else if (distance < neighbor.Distance)
                {
                    int oldPriority = neighbor.SearchPriority;
                    neighbor.Distance = distance;
                    neighbor.PathFrom = current;
                    searchFrontier.Change(neighbor, oldPriority);
                }
            }
        }

        return false;
    }

    void ShowPath(int speed)
    {
        if (currentPathExists && currentPathFrom != currentPathTo)
        {
            HexCell current = currentPathTo;
            while (current != currentPathFrom)
            {
                current.EnableHighlight(HexGameUI.pathColor);
                current = current.PathFrom;
            }
            currentPathTo.SetLabel(currentPathTo.Distance.ToString());
            currentPathTo.EnableHighlight(HexGameUI.toColor);
        }
        else
        {
            currentPathTo.EnableHighlight(HexGameUI.unableColor);
        }

        currentPathFrom.EnableHighlight(HexGameUI.selectedColor);
    }

    public List<HexCell> GetPath()
    {
        if (!currentPathExists)
        {
            return null;
        }

        List<HexCell> path = ListPool<HexCell>.Get();
        for (HexCell c = currentPathTo; c != currentPathFrom; c = c.PathFrom)
        {
            path.Add(c);
        }
        path.Add(currentPathFrom);
        path.Reverse();

        return path;
    }

    public void ClearPath()
    {
        if (currentPathExists)
        {
            HexCell current = currentPathTo;
            while (current != currentPathFrom)
            {
                current.SetLabel(null);
                current.DisableHighlight();
                current = current.PathFrom;
            }
            current.DisableHighlight();
            currentPathExists = false;
        }
        else if (currentPathFrom)
        {
            currentPathFrom.DisableHighlight();
            currentPathTo.DisableHighlight();
        }

        currentPathFrom = currentPathTo = null;
    }

    void ClearUnits()
    {
        for (int i = 0; i < units.Count; i++)
        {
            units[i].Die();
        }
        units.Clear();
    }

    public void AddUnit(HexUnit unit, HexCell location)
    {
        cellShaderData.ImmediateMode = true;
        units.Add(unit);
        unit.Grid = this;
        unit.transform.SetParent(transform, false);
        unit.Location = location;
        unit.Orientation = Random.Range(0f, 360f);
        cellShaderData.ImmediateMode = false;

        if (unit.Owned)
        {
            HexMapCamera.SetPosition(unit.Location);
        }
    }

    public void RemoveUnit(HexUnit unit)
    {
        units.Remove(unit);
        unit.Die();
    }

    List<HexCell> GetVisibleCells(HexCell fromCell, int range)
    {
        List<HexCell> visibleCells = ListPool<HexCell>.Get();

        searchFrontierPhase += 2;

        if (searchFrontier == null)
        {
            searchFrontier = new HexCellPriorityQueue();
        }
        else
        {
            searchFrontier.Clear();
        }

        range += fromCell.ViewElevation;
        fromCell.SearchPhase = searchFrontierPhase;
        fromCell.Distance = 0;
        searchFrontier.Enqueue(fromCell);
        HexCoordinates fromCoordinates = fromCell.coordinates;
        while (searchFrontier.Count > 0)
        {
            HexCell current = searchFrontier.Dequeue();
            current.SearchPhase += 1;
            visibleCells.Add(current);

            for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
            {
                HexCell neighbor = current.GetNeighbor(d);
                // skip null or already calculated neighbor
                if (neighbor == null || neighbor.SearchPhase > searchFrontierPhase || !neighbor.Explorable)
                {
                    continue;
                }

                // update distance
                int distance = current.Distance + 1;
                if ((distance > 3 && distance + neighbor.ViewElevation > range) || distance > fromCoordinates.DistanceTo(neighbor.coordinates))
                {
                    continue;
                }

                if (neighbor.SearchPhase < searchFrontierPhase)
                {
                    neighbor.SearchPhase = searchFrontierPhase;
                    neighbor.Distance = distance;
                    neighbor.SearchHeuristic = 0;
                    searchFrontier.Enqueue(neighbor);
                }
                else if (distance < neighbor.Distance)
                {
                    int oldPriority = neighbor.SearchPriority;
                    neighbor.Distance = distance;
                    searchFrontier.Change(neighbor, oldPriority);
                }
            }
        }

        return visibleCells;
    }

    public void IncreaseVisibility(HexCell fromCell, int range)
    {
        List<HexCell> cells = GetVisibleCells(fromCell, range);
        for (int i = 0; i < cells.Count; i++)
        {
            HexCell cell = cells[i];
            cell.IncreaseVisibility();
        }
        ListPool<HexCell>.Add(cells);
    }

    public void DecreaseVisibility(HexCell fromCell, int range)
    {
        List<HexCell> cells = GetVisibleCells(fromCell, range);
        for (int i = 0; i < cells.Count; i++)
        {
            HexCell cell = cells[i];
            cells[i].DecreaseVisibility();
        }
        ListPool<HexCell>.Add(cells);
    }

    public void ResetVisibility()
    {
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i].ResetVisibility();
        }

        for (int i = 0; i < units.Count; i++)
        {
            HexUnit unit = units[i];
            if (unit.Owned)
            {
                IncreaseVisibility(unit.Location, unit.VisionRange);
            }
        }
    }

    public void AddItem(HexItem item, HexCell location)
    {
        items.Add(item);
        item.Grid = this;
        item.transform.SetParent(transform, false);
        item.Location = location;
        item.InstantiateItem();

        if (item.itemType == HexItemType.Treasure)
        {
            Log.Dev(GetType(), "treasure at " + location.coordinates.ToString());
        }
    }

    public void RemoveItem(int cellIndex)
    {
        HexItem item = cells[cellIndex].Item;
        items.Remove(item);
        item.RemoveFromMap();
    }

    public void changeUnits()
    {
        HexCell temp1 = units[0].Location;
        HexCell temp2 = units[1].Location;
        units[0].Location = null;
        units[1].Location = null;
        units[0].Location = temp2;
        units[1].Location = temp1;
    }

    public void SendRemoveItem(HexItem item)
    {
        photonView.RPC("GetRemoveItem", RpcTarget.Others, item.Location.Index);

        RemoveItem(item.Location.Index);
    }

    [PunRPC]
    void GetRemoveItem(int cellIndex)
    {
        RemoveItemInfo.Synced = true;
        RemoveItemInfo.CellIndex = cellIndex;
    }
}

﻿using UnityEngine;
using UnityEditor;
using System.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
public enum PathEditorMode { NONE, PATH_CREATION }

[CustomEditor(typeof(PathInMaze))]
public class PathEditor : AMazeEditor {

    PathInMaze instance;

    private LinkedList<MazeUnit> pathInSelection;

    private string pathElementPattern = "{0} {1} = {2} turn {3}";
    
    private bool PathCreationEnabled; 
    public PathEditorMode ActiveMode { get; set; }
    PathInMaze pathShouldBeRemoved;

    private bool showElements;

    public void OnEnable()
    {
        instance = target as PathInMaze;

        if (instance == null)
            return;
        if (instance != null){
            maze = instance.GetComponent<beMobileMaze>(); 
        }

        if(instance.PathElements == null)
            instance.PathElements = new Dictionary<Vector2, PathElement>();

        instance.EditorGizmoCallbacks += RenderTileHighlighting;
        instance.EditorGizmoCallbacks += RenderEditorGizmos; 
    }

    public void OnDisable()
    {
        if (instance == null)
            return;

        instance.EditorGizmoCallbacks -= RenderTileHighlighting;
        instance.EditorGizmoCallbacks -= RenderEditorGizmos; 
    }

    public override void OnInspectorGUI()
    {
        instance = target as PathInMaze;

        if (instance != null) {  
            maze = instance.GetComponent<beMobileMaze>();
        }
        if(maze == null) throw new MissingComponentException(string.Format("The Path Controller should be attached to a {0} instance", typeof(beMobileMaze).Name));

        base.OnInspectorGUI();

        EditorGUILayout.BeginVertical();

        PathCreationEnabled = GUILayout.Toggle(PathCreationEnabled, "Path creation");
        
        showElements = EditorGUILayout.Foldout(showElements, "Show Elements");
        
        if(showElements)
            RenderElements();

        if (GUILayout.Button("Reverse Path"))
        {
            instance.InvertPath();
        }

        EditorGUILayout.Separator();
        
        EditorGUILayout.EndVertical();

        if (EditorModeProcessEvent != null)
            EditorModeProcessEvent(Event.current);
    }
    
    private void RenderElements()
    {
        EditorGUILayout.BeginVertical();

        if (GUILayout.Button("Save Path"))
        {
            EditorUtility.SetDirty(instance);
            EditorApplication.delayCall += AssetDatabase.SaveAssets;
        }

        foreach (var e in instance.PathElements)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                string.Format(pathElementPattern, e.Key.x, e.Key.y, Enum.GetName(typeof(UnitType), e.Value.Type), Enum.GetName(typeof(TurnType), e.Value.Turn)), GUILayout.Width(150f));
            
            EditorGUILayout.ObjectField(e.Value.Unit, typeof(MazeUnit), false);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    protected new void RenderTileHighlighting()
    {
        var temp = Gizmos.matrix;
        Gizmos.matrix = maze.transform.localToWorldMatrix;
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(MarkerPosition + new Vector3(0, maze.RoomDimension.y / 2, 0), new Vector3(maze.RoomDimension.x, maze.RoomDimension.y, maze.RoomDimension.z) * 1.1f);
        Gizmos.matrix = temp;
    }

    public override void RenderSceneViewUI()
    {
        Handles.BeginGUI();

        #region Path creation mode

        EditorGUILayout.BeginVertical(GUILayout.Width(100f));
        
        if (PathCreationEnabled)
        { 
            if (ActiveMode != PathEditorMode.PATH_CREATION)
            { 
                pathInSelection = new LinkedList<MazeUnit>();

                EditorModeProcessEvent += PathCreationMode;
                ActiveMode = PathEditorMode.PATH_CREATION;
            }

            GUILayout.Space(4f);
            
        }
        else
        {
            EditorModeProcessEvent -= PathCreationMode;

            if (pathInSelection != null)
                pathInSelection.Clear();

        }


        EditorGUILayout.EndVertical();

        #endregion
        Handles.EndGUI();
    }
     
    #region path creation logic
    private void PathCreationMode(Event _ce)
    {
        int controlId = GUIUtility.GetControlID(FocusType.Passive);

        if (_ce.type == EventType.MouseDown || _ce.type == EventType.MouseDrag)
        {
            var unit = maze.Grid[Mathf.FloorToInt(currentTilePosition.x), Mathf.FloorToInt(currentTilePosition.y)];

            if (unit == null)
            {
                Debug.Log("no element added");
                
                GUIUtility.hotControl = controlId;
                _ce.Use();

                return;
            }

            if (_ce.button == 0)
            {
                Add(unit);

                EditorUtility.SetDirty(instance);
            }
            if (_ce.button == 1 && instance.PathElements.Any())
            {
                Remove(unit);

                EditorUtility.SetDirty(instance);
            } 

            GUIUtility.hotControl = controlId;
            _ce.Use();
        }
    }

    private void Add(MazeUnit newUnit)
    {
        /**
         * 
         * BUG Dictionary sorts the keys! So the Path will be messed up!
         * */
          
        if (instance.PathElements.ContainsKey(newUnit.GridID))
            return;

        var newElement = new PathElement(newUnit);
        
        newElement = GetElementType(newElement);

        //if (newElement.Type == UnitType.L || newElement.Type == UnitType.T || newElement.Type == UnitType.X) {
        //    var previousElement = instance.PathElements.Values.Last();
        //    newElement = GetTurnType(newElement, previousElement);

        //    DeployLandmark(previousElement);
        //}

        //if (newElement.Type == UnitType.L || newElement.Type == UnitType.T || newElement.Type == UnitType.X) { 
        var nr_el = instance.PathElements.Values.Count; // count all elements in the path to get the second last for turning calculation
        if (nr_el >= 1)
        {
            var previousElement = instance.PathElements.Values.ElementAt(nr_el - 1);
            if (nr_el >= 2)
            {
                var secpreviousElement = instance.PathElements.Values.ElementAt(nr_el - 2);
                newElement = GetTurnType(newElement, previousElement, secpreviousElement);
            }
            else
            {
                newElement.Turn = TurnType.STRAIGHT;
            } 
        }
        //}

        instance.PathElements.Add(newUnit.GridID, newElement);
    }
    

    public static PathElement GetElementType(PathElement element)
    {
        var u = element.Unit;

        if (u.WaysOpen == (OpenDirections.East | OpenDirections.West) ||
            u.WaysOpen == (OpenDirections.North | OpenDirections.South) || 
            u.WaysOpen == OpenDirections.East ||
            u.WaysOpen == OpenDirections.West ||
            u.WaysOpen == OpenDirections.North ||
            u.WaysOpen == OpenDirections.South )
        {
           element.Type = UnitType.I;
        }
        
        if(u.WaysOpen == OpenDirections.All)
            element.Type = UnitType.X;

        if(u.WaysOpen == (OpenDirections.West | OpenDirections.North | OpenDirections.East) ||
           u.WaysOpen ==  (OpenDirections.West | OpenDirections.South | OpenDirections.East) ||
           u.WaysOpen == (OpenDirections.West | OpenDirections.South | OpenDirections.North) ||
            u.WaysOpen ==  (OpenDirections.East | OpenDirections.South | OpenDirections.North) )
        {
            element.Type = UnitType.T;
        }

        if(u.WaysOpen == (OpenDirections.West | OpenDirections.North ) ||
           u.WaysOpen ==  (OpenDirections.West | OpenDirections.South ) ||
           u.WaysOpen == (OpenDirections.East | OpenDirections.South ) ||
            u.WaysOpen ==  (OpenDirections.East | OpenDirections.North) )
        {
            element.Type = UnitType.L;
        }

        return element;
    }

    public static PathElement GetTurnType(PathElement current, PathElement last, PathElement sec2last)
    {
        var x0 = sec2last.Unit.GridID.x;
        var y0 = sec2last.Unit.GridID.y;
        //  var x1 = last.Unit.GridID.x; // why unused?
        var y1 = last.Unit.GridID.y;
        var x2 = current.Unit.GridID.x;
        var y2 = current.Unit.GridID.y;

        if ((x0 - x2) - (y0 - y2) == 0) // same sign
        {
            if (y0 != y1) // first change in y
            {
                last.Turn = TurnType.RIGHT;
            }
            else
            {
                last.Turn = TurnType.LEFT;
            }
        }
        else // different sign
        {
            if (y0 != y1) // first change in y
            {
                last.Turn = TurnType.LEFT;
            }
            else
            {
                last.Turn = TurnType.RIGHT;
            }
        }

        if (Math.Abs(x0 - x2) == 2 || Math.Abs(y0 - y2) == 2)
        {
            last.Turn = TurnType.STRAIGHT;
        }

        return current;
    }

    private void Remove(MazeUnit unit)
    {
        instance.PathElements.Remove(unit.GridID);
    }

    public LinkedList<MazeUnit> CreatePathFromGridIDs(LinkedList<Vector2> gridIDs)
    {
        var enumerator = gridIDs.GetEnumerator();
        var units = new LinkedList<MazeUnit>();

        while (enumerator.MoveNext())
        {
            var gridField = enumerator.Current;

            var correspondingUnitHost = maze.transform.FindChild(string.Format("Unit_{0}_{1}", gridField.x, gridField.y));

            if (correspondingUnitHost == null)
                throw new MissingComponentException("It seems, that the path doesn't match the maze! Requested Unit is missing!");

            var unit = correspondingUnitHost.GetComponent<MazeUnit>();

            if (unit == null)
                throw new MissingComponentException("Expected Component on Maze Unit is missing!");

            units.AddLast(unit);
        }

        return units;

    }

    #endregion  

    protected override void RenderEditorGizmos()
    {
        if (!instance.enabled)
            return;

        if (instance.PathElements.Count > 0)
        {
            var hoveringDistance = new Vector3(0f, maze.RoomDimension.y, 0f);

            var start = instance.PathElements.First().Value.Unit.transform;
            Handles.color = Color.blue;
            Handles.CubeCap(this.GetInstanceID(), start.position + hoveringDistance, start.rotation, 0.3f);


            var iterator = instance.PathElements.Values.GetEnumerator();
            MazeUnit last = null;

            while (iterator.MoveNext())
            {
                if (last == null)
                {
                    last = iterator.Current.Unit;
                    continue;
                }

                if (last == null || iterator.Current.Unit == null) { 
                    last = iterator.Current.Unit;
                    continue;
                }

                Gizmos.DrawLine(last.transform.position + hoveringDistance, iterator.Current.Unit.transform.position + hoveringDistance);

                last = iterator.Current.Unit;
            }
            
            var lastElement = instance.PathElements.Last().Value.Unit;
            var endTransform = lastElement.transform;

            var coneRotation =  start.rotation;

            switch (lastElement.WaysOpen)
            {
                case OpenDirections.None:
                    break;
                case OpenDirections.North:
                    coneRotation.SetLookRotation(-endTransform.forward);
                    break;
                case OpenDirections.South:
                    coneRotation.SetLookRotation(endTransform.forward);
                    break;
                case OpenDirections.East:
                    coneRotation.SetLookRotation(-endTransform.right);
                    break;
                case OpenDirections.West:
                    coneRotation.SetLookRotation(endTransform.right);
                    break;
                case OpenDirections.All:
                    break;
                default:
                    break;
            }    
           
            Handles.ConeCap(this.GetInstanceID(), endTransform.position + hoveringDistance, coneRotation, 0.3f);
        }
    } 
}

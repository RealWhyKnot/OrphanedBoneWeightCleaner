using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;

public class OrphanedBoneWeightCleaner : EditorWindow
{
    private bool autoGrowIslands = true;
    private Vector2 scrollPos;
    private string logOutput = "Ready... Select a GameObject with a SkinnedMeshRenderer to begin.";
    private Stopwatch stopwatch = new Stopwatch();

    [MenuItem("Tools/WhyKnot/Clean Mesh By Orphaned Bone Weights")]
    private static void ShowWindow()
    {
        OrphanedBoneWeightCleaner window = GetWindow<OrphanedBoneWeightCleaner>("Clean Mesh");
        window.minSize = new Vector2(400, 350);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Orphaned Bone Weight Cleaner", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Removes vertices with invalid (orphaned) bone weights and, optionally, any adjacent vertices.", MessageType.Info);

        GameObject selectedObject = Selection.activeGameObject;
        SkinnedMeshRenderer smr = null;

        if (selectedObject == null)
        {
            EditorGUILayout.HelpBox("Please select a GameObject in your scene that has a SkinnedMeshRenderer.", MessageType.Warning);
            logOutput = "Ready... Select a GameObject with a SkinnedMeshRenderer to begin.";
            return;
        }

        smr = selectedObject.GetComponent<SkinnedMeshRenderer>();
        if (smr == null)
        {
            EditorGUILayout.HelpBox("The selected object does not have a SkinnedMeshRenderer component.", MessageType.Error);
            logOutput = $"Error: '{selectedObject.name}' has no SkinnedMeshRenderer.";
            return;
        }

        EditorGUILayout.LabelField("Selected Mesh:", EditorStyles.boldLabel);
        EditorGUILayout.ObjectField("Target SMR", smr, typeof(SkinnedMeshRenderer), true);
        
        if (smr.sharedMesh != null)
        {
            EditorGUILayout.LabelField("Mesh Vertices:", smr.sharedMesh.vertexCount.ToString());
        }
        else
        {
            EditorGUILayout.HelpBox("The selected SkinnedMeshRenderer has no mesh assigned.", MessageType.Error);
            logOutput = "Error: The selected SkinnedMeshRenderer has no mesh assigned.";
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
        
        autoGrowIslands = EditorGUILayout.Toggle(
            new GUIContent("Auto-Remove Islands", "Automatically grows the removal selection to delete any 'islands' or 'stubs' (like your tail) left behind after removing orphans."),
            autoGrowIslands);

        EditorGUILayout.Space();
        
        if (GUILayout.Button("Clean Mesh"))
        {
            logOutput = "Starting clean process...\n";
            Repaint();
            stopwatch.Restart();
            
            // --- THIS LINE WAS FIXED ---
            logOutput += $"[GUI] Calling CleanMesh_Internal with autoGrow={autoGrowIslands}.\n";
            CleanMesh_Internal(smr, autoGrowIslands, (log) => {
                logOutput += log + "\n";
                scrollPos.y = float.MaxValue; 
                Repaint();
            });
            
            stopwatch.Stop();
            logOutput += $"--- PROCESS FINISHED in {stopwatch.Elapsed.TotalSeconds:F2} seconds ---";
            scrollPos.y = float.MaxValue;
            Repaint();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Log Output:", EditorStyles.boldLabel);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
        EditorGUILayout.TextArea(logOutput, EditorStyles.textArea, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private static void CleanMesh_Internal(SkinnedMeshRenderer skinnedMeshRenderer, bool autoGrow, System.Action<string> logger)
    {
        logger("========================================================");
        logger("--- SCRIPT STARTED: Clean Mesh By Orphaned Bone Weights ---");
        logger("========================================================");

        GameObject selectedObject = skinnedMeshRenderer.gameObject;
        logger($"[SETUP] Target object: '{selectedObject.name}'.");
        logger($"[SETUP] Found SkinnedMeshRenderer on '{skinnedMeshRenderer.name}'.");

        Mesh originalMesh = skinnedMeshRenderer.sharedMesh;
        if (originalMesh == null)
        {
            logger("[SETUP] FAILED: The SkinnedMeshRenderer has no 'sharedMesh' assigned. Aborting script.");
            return;
        }
        
        if (!originalMesh.isReadable)
        {
            logger($"[SETUP] FAILED: The mesh '{originalMesh.name}' is not readable. Please enable 'Read/Write' in its import settings. Aborting script.");
            EditorUtility.DisplayDialog("Mesh Not Readable", $"The mesh '{originalMesh.name}' is not readable. Please enable 'Read/Write' in its import settings.", "OK");
            return;
        }

        logger($"[SETUP] Found original mesh: '{originalMesh.name}'. isReadable={originalMesh.isReadable}");
        logger($"[SETUP] Original Mesh Stats | Vertices: {originalMesh.vertexCount}, Sub-meshes: {originalMesh.subMeshCount}, Blendshapes: {originalMesh.blendShapeCount}, Materials: {skinnedMeshRenderer.sharedMaterials.Length}");


        logger("[DATA] Reading all data arrays from the original mesh...");
        Vector3[] originalVertices = originalMesh.vertices;
        logger($"[DATA] -> Read {originalVertices.Length} vertices.");
        BoneWeight[] originalBoneWeights = originalMesh.boneWeights;
        logger($"[DATA] -> Read {originalBoneWeights.Length} bone weights.");
        Transform[] bones = skinnedMeshRenderer.bones;
        logger($"[DATA] -> Read {bones.Length} bones from SkinnedMeshRenderer.");
        Matrix4x4[] originalBindposes = originalMesh.bindposes;
        logger($"[DATA] -> Read {originalBindposes.Length} bindposes from mesh.");

        if (originalVertices.Length != originalBoneWeights.Length)
        {
             logger($"[DATA] FAILED: Vertex count ({originalVertices.Length}) does not match bone weight count ({originalBoneWeights.Length}). Aborting.");
             return;
        }

        bool hasNormals = originalMesh.normals.Length > 0;
        bool hasTangents = originalMesh.tangents.Length > 0;
        bool hasColors = originalMesh.colors.Length > 0;
        bool hasUVs = originalMesh.uv.Length > 0;
        bool hasUV2s = originalMesh.uv2.Length > 0;
        bool hasUV3s = originalMesh.uv3.Length > 0;
        bool hasUV4s = originalMesh.uv4.Length > 0;
        bool hasUV5s = originalMesh.uv5.Length > 0;
        bool hasUV6s = originalMesh.uv6.Length > 0;
        bool hasUV7s = originalMesh.uv7.Length > 0;
        bool hasUV8s = originalMesh.uv8.Length > 0;
        logger($"[DATA] Mesh attribute check: hasNormals={hasNormals}, hasTangents={hasTangents}, hasColors={hasColors}");
        logger($"[DATA] UV check: UV0={hasUVs}, UV1={hasUV2s}, UV2={hasUV3s}, UV3={hasUV4s}, UV4={hasUV5s}, UV5={hasUV6s}, UV6={hasUV7s}, UV7={hasUV8s}");

        Vector3[] originalNormals = hasNormals ? originalMesh.normals : null;
        Vector4[] originalTangents = hasTangents ? originalMesh.tangents : null;
        Color[] originalColors = hasColors ? originalMesh.colors : null;
        Vector2[] originalUVs = hasUVs ? originalMesh.uv : null;
        Vector2[] originalUV2s = hasUV2s ? originalMesh.uv2 : null;
        Vector2[] originalUV3s = hasUV3s ? originalMesh.uv3 : null;
        Vector2[] originalUV4s = hasUV4s ? originalMesh.uv4 : null;
        Vector2[] originalUV5s = hasUV5s ? originalMesh.uv5 : null;
        Vector2[] originalUV6s = hasUV6s ? originalMesh.uv6 : null;
        Vector2[] originalUV7s = hasUV7s ? originalMesh.uv7 : null;
        Vector2[] originalUV8s = hasUV8s ? originalMesh.uv8 : null;

        if (hasUVs) logger($"[DATA-UV] -> Read {originalUVs.Length} vertices for UV0 (uv)");
        if (hasUV2s) logger($"[DATA-UV] -> Read {originalUV2s.Length} vertices for UV1 (uv2)");
        if (hasUV3s) logger($"[DATA-UV] -> Read {originalUV3s.Length} vertices for UV2 (uv3)");
        if (hasUV4s) logger($"[DATA-UV] -> Read {originalUV4s.Length} vertices for UV3 (uv4)");
        if (hasUV5s) logger($"[DATA-UV] -> Read {originalUV5s.Length} vertices for UV4 (uv5)");
        if (hasUV6s) logger($"[DATA-UV] -> Read {originalUV6s.Length} vertices for UV5 (uv6)");
        if (hasUV7s) logger($"[DATA-UV] -> Read {originalUV7s.Length} vertices for UV6 (uv7)");
        if (hasUV8s) logger($"[DATA-UV] -> Read {originalUV8s.Length} vertices for UV7 (uv8)");

        logger("[ANALYSIS] Starting analysis of bone weights for each vertex...");
        List<int> verticesToKeep = new List<int>();
        List<int> verticesToRemove = new List<int>();
        int boneCount = bones.Length;
        bool loggedFirstOrphan = false;

        for (int i = 0; i < originalBoneWeights.Length; i++)
        {
            if (i > 0 && i % 25000 == 0) logger($"[ANALYSIS] ...processed {i} / {originalBoneWeights.Length} vertices...");

            BoneWeight bw = originalBoneWeights[i];
            string reason = "";
            bool hasInvalidBone = false;

            if (bw.weight0 > 0 && (bw.boneIndex0 >= boneCount || bones[bw.boneIndex0] == null)) { hasInvalidBone = true; reason = $"boneIndex0 ({bw.boneIndex0})"; }
            if (!hasInvalidBone && bw.weight1 > 0 && (bw.boneIndex1 >= boneCount || bones[bw.boneIndex1] == null)) { hasInvalidBone = true; reason = $"boneIndex1 ({bw.boneIndex1})"; }
            if (!hasInvalidBone && bw.weight2 > 0 && (bw.boneIndex2 >= boneCount || bones[bw.boneIndex2] == null)) { hasInvalidBone = true; reason = $"boneIndex2 ({bw.boneIndex2})"; }
            if (!hasInvalidBone && bw.weight3 > 0 && (bw.boneIndex3 >= boneCount || bones[bw.boneIndex3] == null)) { hasInvalidBone = true; reason = $"boneIndex3 ({bw.boneIndex3})"; }

            if (hasInvalidBone)
            {
                verticesToRemove.Add(i);
                if (!loggedFirstOrphan)
                {
                    logger($"[ANALYSIS] Found first orphaned vertex at index {i}. Reason: {reason} is out of bounds (bone count is {boneCount}) or bone transform is null.");
                    loggedFirstOrphan = true;
                }
            }
            else
            {
                verticesToKeep.Add(i);
            }
        }
        logger($"[ANALYSIS] Vertex analysis complete. Vertices to keep: {verticesToKeep.Count}. Vertices to remove: {verticesToRemove.Count}.");

        if (autoGrow && verticesToRemove.Count > 0 && verticesToKeep.Count > 0)
        {
            logger($"[GROW] Starting auto-grow to remove islands...");

            HashSet<int> verticesToRemoveSet_Grow = new HashSet<int>(verticesToRemove);
            HashSet<int> verticesToKeepSet_Grow = new HashSet<int>(verticesToKeep);

            List<int[]> allTriangles = new List<int[]>();
            int totalTriangleCount = 0;
            for (int i = 0; i < originalMesh.subMeshCount; i++)
            {
                int[] subTriangles = originalMesh.GetTriangles(i);
                allTriangles.Add(subTriangles);
                totalTriangleCount += subTriangles.Length / 3;
            }
            logger($"[GROW] Found {totalTriangleCount} total triangles across {originalMesh.subMeshCount} submeshes.");

            int iter = 0;
            while (true)
            {
                logger($"[GROW] Auto-grow iteration {iter + 1}...");
                HashSet<int> newlyRemoved = new HashSet<int>();

                foreach (int[] triangles in allTriangles)
                {
                    for (int j = 0; j < triangles.Length; j += 3)
                    {
                        int v1 = triangles[j];
                        int v2 = triangles[j + 1];
                        int v3 = triangles[j + 2];

                        bool v1Bad = verticesToRemoveSet_Grow.Contains(v1);
                        bool v2Bad = verticesToRemoveSet_Grow.Contains(v2);
                        bool v3Bad = verticesToRemoveSet_Grow.Contains(v3);

                        bool hasBad = v1Bad || v2Bad || v3Bad;
                        bool allBad = v1Bad && v2Bad && v3Bad;
                        
                        if (hasBad && !allBad)
                        {
                            if (!v1Bad && verticesToKeepSet_Grow.Contains(v1)) newlyRemoved.Add(v1);
                            if (!v2Bad && verticesToKeepSet_Grow.Contains(v2)) newlyRemoved.Add(v2);
                            if (!v3Bad && verticesToKeepSet_Grow.Contains(v3)) newlyRemoved.Add(v3);
                        }
                    }
                }

                if (newlyRemoved.Count == 0)
                {
                    logger("[GROW] No more vertices found to grow. Stopping.");
                    break;
                }

                logger($"[GROW] -> Adding {newlyRemoved.Count} new vertices to removal list.");
                foreach (int v in newlyRemoved)
                {
                    verticesToRemoveSet_Grow.Add(v);
                    verticesToKeepSet_Grow.Remove(v);
                }
                
                if (verticesToKeepSet_Grow.Count == 0)
                {
                    logger("[GROW] All vertices have been marked for removal. Stopping grow loop.");
                    break;
                }
                iter++;
            }

            verticesToKeep = new List<int>(verticesToKeepSet_Grow);
            verticesToRemove = new List<int>(verticesToRemoveSet_Grow);

            logger($"[GROW] Grow pass complete. Final vertices to keep: {verticesToKeep.Count}. Final vertices to remove: {verticesToRemove.Count}.");
        }

        if (verticesToKeep.Count == originalVertices.Length)
        {
            logger("[ANALYSIS] No orphaned bone weights found (and no grow iterations). Aborting script as no changes are needed.");
            EditorUtility.DisplayDialog("No Orphaned Weights Found", "All vertices are weighted to existing bones. No changes were made.", "OK");
            return;
        }
        if (verticesToKeep.Count == 0)
        {
            logger("[ANALYSIS] FAILED: No validly weighted vertices found. Aborting script.");
            EditorUtility.DisplayDialog("No Validly Weighted Vertices", "No vertices are weighted to existing bones. Cannot create a new mesh.", "OK");
            return;
        }

        logger("[MAPPING] Creating old-to-new vertex index map...");
        int[] oldToNewVertexIndexMap = new int[originalVertices.Length];
        for (int i = 0; i < oldToNewVertexIndexMap.Length; i++) oldToNewVertexIndexMap[i] = -1;
        for (int i = 0; i < verticesToKeep.Count; i++)
        {
            oldToNewVertexIndexMap[verticesToKeep[i]] = i;
        }
        logger("[MAPPING] Index map created.");

        logger("[MAPPING] Populating new data lists for kept vertices...");
        List<Vector3> newVertices = new List<Vector3>(verticesToKeep.Count);
        List<BoneWeight> newBoneWeights = new List<BoneWeight>(verticesToKeep.Count);
        List<Vector3> newNormals = hasNormals ? new List<Vector3>(verticesToKeep.Count) : null;
        List<Vector4> newTangents = hasTangents ? new List<Vector4>(verticesToKeep.Count) : null;
        List<Color> newColors = hasColors ? new List<Color>(verticesToKeep.Count) : null;
        List<Vector2> newUVs = hasUVs ? new List<Vector2>(verticesToKeep.Count) : null;
        List<Vector2> newUV2s = hasUV2s ? new List<Vector2>(verticesToKeep.Count) : null;
        List<Vector2> newUV3s = hasUV3s ? new List<Vector2>(verticesToKeep.Count) : null;
        List<Vector2> newUV4s = hasUV4s ? new List<Vector2>(verticesToKeep.Count) : null;
        List<Vector2> newUV5s = hasUV5s ? new List<Vector2>(verticesToKeep.Count) : null;
        List<Vector2> newUV6s = hasUV6s ? new List<Vector2>(verticesToKeep.Count) : null;
        List<Vector2> newUV7s = hasUV7s ? new List<Vector2>(verticesToKeep.Count) : null;
        List<Vector2> newUV8s = hasUV8s ? new List<Vector2>(verticesToKeep.Count) : null;

        int mappingCounter = 0;
        foreach (int oldIndex in verticesToKeep)
        {
            if (mappingCounter > 0 && mappingCounter % 25000 == 0) logger($"[MAPPING] ...processed {mappingCounter} / {verticesToKeep.Count} vertices...");
            
            newVertices.Add(originalVertices[oldIndex]);
            newBoneWeights.Add(originalBoneWeights[oldIndex]);
            if (hasNormals) newNormals.Add(originalNormals[oldIndex]);
            if (hasTangents) newTangents.Add(originalTangents[oldIndex]);
            if (hasColors) newColors.Add(originalColors[oldIndex]);
            if (hasUVs) newUVs.Add(originalUVs[oldIndex]);
            if (hasUV2s) newUV2s.Add(originalUV2s[oldIndex]);
            if (hasUV3s) newUV3s.Add(originalUV3s[oldIndex]);
            if (hasUV4s) newUV4s.Add(originalUV4s[oldIndex]);
            if (hasUV5s) newUV5s.Add(originalUV5s[oldIndex]);
            if (hasUV6s) newUV6s.Add(originalUV6s[oldIndex]);
            if (hasUV7s) newUV7s.Add(originalUV7s[oldIndex]);
            if (hasUV8s) newUV8s.Add(originalUV8s[oldIndex]);
            
            mappingCounter++;
        }
        logger("[MAPPING] New data lists populated.");
        logger($"[MAPPING] New list counts | Vertices: {newVertices.Count}, BoneWeights: {newBoneWeights.Count}, Normals: {newNormals?.Count ?? 0}, UV0: {newUVs?.Count ?? 0}, UV1: {newUV2s?.Count ?? 0}");

        logger("--- [SANITY CHECK PRE-BUILD] ---");
        int[] checkIndices = { 0, verticesToKeep.Count / 4, verticesToKeep.Count / 2, (verticesToKeep.Count / 4) * 3, verticesToKeep.Count - 1 };
        foreach (int newIndex in checkIndices)
        {
            if (newIndex < 0 || newIndex >= verticesToKeep.Count) continue;
            int oldIndex = verticesToKeep[newIndex];
            var sb = new StringBuilder();
            sb.AppendLine($"--- Checking New Index {newIndex} (from Old Index {oldIndex}) ---");
            sb.AppendLine($"    POS: Old={originalVertices[oldIndex]} | New={newVertices[newIndex]} | Match={originalVertices[oldIndex] == newVertices[newIndex]}");
            if (hasNormals) sb.AppendLine($"    NRM: Old={originalNormals[oldIndex]} | New={newNormals[newIndex]} | Match={originalNormals[oldIndex] == newNormals[newIndex]}");
            if (hasUVs) sb.AppendLine($"    UV0: Old={originalUVs[oldIndex]} | New={newUVs[newIndex]} | Match={originalUVs[oldIndex] == newUVs[newIndex]}");
            if (hasUV2s) sb.AppendLine($"    UV1: Old={originalUV2s[oldIndex]} | New={newUV2s[newIndex]} | Match={originalUV2s[oldIndex] == newUV2s[newIndex]}");
            if (hasUV3s) sb.AppendLine($"    UV2: Old={originalUV3s[oldIndex]} | New={newUV3s[newIndex]} | Match={originalUV3s[oldIndex] == newUV3s[newIndex]}");
            if (hasUV4s) sb.AppendLine($"    UV3: Old={originalUV4s[oldIndex]} | New={newUV4s[newIndex]} | Match={originalUV4s[oldIndex] == newUV4s[newIndex]}");
            if (hasUV5s) sb.AppendLine($"    UV4: Old={originalUV5s[oldIndex]} | New={newUV5s[newIndex]} | Match={originalUV5s[oldIndex] == newUV5s[newIndex]}");
            if (hasUV6s) sb.AppendLine($"    UV5: Old={originalUV6s[oldIndex]} | New={newUV6s[newIndex]} | Match={originalUV6s[oldIndex] == newUV6s[newIndex]}");
            if (hasUV7s) sb.AppendLine($"    UV6: Old={originalUV7s[oldIndex]} | New={newUV7s[newIndex]} | Match={originalUV7s[oldIndex] == newUV7s[newIndex]}");
            if (hasUV8s) sb.AppendLine($"    UV7: Old={originalUV8s[oldIndex]} | New={newUV8s[newIndex]} | Match={originalUV8s[oldIndex] == newUV8s[newIndex]}");
            sb.AppendLine($"    B_W: Old=(b{originalBoneWeights[oldIndex].boneIndex0}, w{originalBoneWeights[oldIndex].weight0:F2}) | New=(b{newBoneWeights[newIndex].boneIndex0}, w{newBoneWeights[newIndex].weight0:F2}) | Match={originalBoneWeights[oldIndex].Equals(newBoneWeights[newIndex])}");
            logger(sb.ToString());
        }
        logger("--- [SANITY CHECK PRE-BUILD COMPLETE] ---");


        logger("[TRIANGLES] Rebuilding triangles for each sub-mesh...");
        List<List<int>> newSubmeshTriangles = new List<List<int>>();
        List<Material> newMaterials = new List<Material>();
        Material[] originalMaterials = skinnedMeshRenderer.sharedMaterials;
        
        HashSet<int> verticesToKeepSet = new HashSet<int>(verticesToKeep);
        int totalTrianglesKept = 0;
        int totalTrianglesRemoved = 0;

        for (int i = 0; i < originalMesh.subMeshCount; i++)
        {
            logger($"[TRIANGLES] Processing Sub-mesh {i}/{originalMesh.subMeshCount - 1}...");
            int[] originalSubTriangles = originalMesh.GetTriangles(i);
            int originalSubmeshTriangleCount = originalSubTriangles.Length / 3;
            logger($"[TRIANGLES] -> Sub-mesh {i} has {originalSubmeshTriangleCount} triangles.");
            List<int> newTrianglesForSubmesh = new List<int>();

            for (int j = 0; j < originalSubTriangles.Length; j += 3)
            {
                int oldV1 = originalSubTriangles[j];
                int oldV2 = originalSubTriangles[j + 1];
                int oldV3 = originalSubTriangles[j + 2];

                if (verticesToKeepSet.Contains(oldV1) && verticesToKeepSet.Contains(oldV2) && verticesToKeepSet.Contains(oldV3))
                {
                    newTrianglesForSubmesh.Add(oldToNewVertexIndexMap[oldV1]);
                    newTrianglesForSubmesh.Add(oldToNewVertexIndexMap[oldV2]);
                    newTrianglesForSubmesh.Add(oldToNewVertexIndexMap[oldV3]);
                }
            }

            int newSubmeshTriangleCount = newTrianglesForSubmesh.Count / 3;
            int removedSubmeshTriangleCount = originalSubmeshTriangleCount - newSubmeshTriangleCount;
            totalTrianglesKept += newSubmeshTriangleCount;
            totalTrianglesRemoved += removedSubmeshTriangleCount;

            if (newTrianglesForSubmesh.Count > 0)
            {
                newSubmeshTriangles.Add(newTrianglesForSubmesh);
                Material mat = (i < originalMaterials.Length) ? originalMaterials[i] : null;
                newMaterials.Add(mat);
                logger($"[TRIANGLES] -> Sub-mesh {i} KEPT. New triangle count: {newSubmeshTriangleCount} (Removed {removedSubmeshTriangleCount}). Associated material: '{(mat != null ? mat.name : "NULL")}'");
            }
            else
            {
                logger($"[TRIANGLES] -> Sub-mesh {i} DISCARDED as it has no valid triangles left. (Removed {removedSubmeshTriangleCount} triangles).");
            }
        }
        logger($"[TRIANGLES] Triangle rebuilding complete. Total Triangles Kept: {totalTrianglesKept}. Total Triangles Removed: {totalTrianglesRemoved}.");

        logger("[CREATION] Creating new Mesh object...");
        Mesh newMesh = new Mesh();
        newMesh.name = originalMesh.name + "_cleaned";
        logger($"[CREATION] -> Name set to '{newMesh.name}'.");

        if (newVertices.Count > 65535)
        {
            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            logger("[CREATION] -> Vertex count > 65535. Set index format to UInt32.");
        }
        else
        {
            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            logger("[CREATION] -> Vertex count <= 65535. Set index format to UInt16.");
        }

        logger($"[CREATION] Assigning {newVertices.Count} vertices...");
        newMesh.SetVertices(newVertices);
        logger($"[CREATION] Assigning {newBoneWeights.Count} bone weights...");
        newMesh.boneWeights = newBoneWeights.ToArray();
        if (hasNormals) { logger($"[CREATION] Assigning {newNormals.Count} normals..."); newMesh.SetNormals(newNormals); }
        if (hasTangents) { logger($"[CREATION] Assigning {newTangents.Count} tangents..."); newMesh.SetTangents(newTangents); }
        if (hasColors) { logger($"[CREATION] Assigning {newColors.Count} colors..."); newMesh.SetColors(newColors); }

        if (hasUVs) { logger($"[CREATION] Assigning {newUVs.Count} UV0s..."); newMesh.SetUVs(0, newUVs); }
        if (hasUV2s) { logger($"[CREATION] Assigning {newUV2s.Count} UV1s..."); newMesh.SetUVs(1, newUV2s); }
        if (hasUV3s) { logger($"[CREATION] Assigning {newUV3s.Count} UV2s..."); newMesh.SetUVs(2, newUV3s); }
        if (hasUV4s) { logger($"[CREATION] Assigning {newUV4s.Count} UV3s..."); newMesh.SetUVs(3, newUV4s); }
        if (hasUV5s) { logger($"[CREATION] Assigning {newUV5s.Count} UV4s..."); newMesh.SetUVs(4, newUV5s); }
        if (hasUV6s) { logger($"[CREATION] Assigning {newUV6s.Count} UV5s..."); newMesh.SetUVs(5, newUV6s); }
        if (hasUV7s) { logger($"[CREATION] Assigning {newUV7s.Count} UV6s..."); newMesh.SetUVs(6, newUV7s); }
        if (hasUV8s) { logger($"[CREATION] Assigning {newUV8s.Count} UV7s..."); newMesh.SetUVs(7, newUV8s); }

        logger($"[CREATION] Assigning triangles for {newSubmeshTriangles.Count} sub-meshes...");
        newMesh.subMeshCount = newSubmeshTriangles.Count;
        for (int i = 0; i < newSubmeshTriangles.Count; i++)
        {
            newMesh.SetTriangles(newSubmeshTriangles[i].ToArray(), i);
            logger($"[CREATION] -> Assigned {newSubmeshTriangles[i].Count / 3} triangles to sub-mesh {i}.");
        }
        logger("[CREATION] Base mesh data assigned.");


        logger("[BLENDSHAPES] Rebuilding blendshapes...");
        newMesh.ClearBlendShapes();
        int blendShapeCount = originalMesh.blendShapeCount;
        if (blendShapeCount > 0)
        {
            logger($"[BLENDSHAPES] Found {blendShapeCount} blendshapes to process.");
            for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
            {
                string shapeName = originalMesh.GetBlendShapeName(shapeIndex);
                int frameCount = originalMesh.GetBlendShapeFrameCount(shapeIndex);
                logger($"[BLENDSHAPES] Processing '{shapeName}' ({shapeIndex + 1}/{blendShapeCount}), which has {frameCount} frame(s).");

                Vector3[] deltaVertices = new Vector3[originalVertices.Length];
                Vector3[] deltaNormals = hasNormals ? new Vector3[originalVertices.Length] : null;
                Vector3[] deltaTangents = hasTangents ? new Vector3[originalVertices.Length] : null;

                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    float frameWeight = originalMesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex);
                    logger($"[BLENDSHAPES] -> Processing frame {frameIndex} with weight {frameWeight}.");
                    originalMesh.GetBlendShapeFrameVertices(shapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);
                    logger($"[BLENDSHAPES] -> -> Got original delta data.");

                    Vector3[] newDeltaVertices = new Vector3[newVertices.Count];
                    Vector3[] newDeltaNormals = hasNormals ? new Vector3[newVertices.Count] : null;
                    Vector3[] newDeltaTangents = hasTangents ? new Vector3[newVertices.Count] : null;

                    for (int i = 0; i < verticesToKeep.Count; i++)
                    {
                        int oldIndex = verticesToKeep[i];
                        newDeltaVertices[i] = deltaVertices[oldIndex];
                        if (hasNormals) newDeltaNormals[i] = deltaNormals[oldIndex];
                        if (hasTangents) newDeltaTangents[i] = deltaTangents[oldIndex];
                    }
                    logger($"[BLENDSHAPES] -> -> Mapped old deltas to new vertex list ({newDeltaVertices.Length} vertices).");

                    foreach (int newIndex in checkIndices)
                    {
                        if (newIndex < 0 || newIndex >= verticesToKeep.Count) continue;
                        int oldIndex = verticesToKeep[newIndex];
                        if (deltaVertices[oldIndex] != Vector3.zero)
                        {
                            bool match = deltaVertices[oldIndex] == newDeltaVertices[newIndex];
                            logger($"[BLENDSHAPES] SANITY CHECK ('{shapeName}') | New Idx: {newIndex} (Old: {oldIndex}) | Match: {match} | Delta: {newDeltaVertices[newIndex]}");
                        }
                    }

                    newMesh.AddBlendShapeFrame(shapeName, frameWeight, newDeltaVertices, newDeltaNormals, newDeltaTangents);
                    logger($"[BLENDSHAPES] -> -> Added frame to new mesh.");
                }
            }
            logger("[BLENDSHAPES] Blendshape processing complete.");
        }
        else
        {
            logger("[BLENDSHAPES] No blendshapes found on original mesh.");
        }

        logger("[FINALIZE] Copying bindposes...");
        newMesh.bindposes = originalBindposes;
        logger("[FINALIZE] Recalculating bounds...");
        newMesh.RecalculateBounds();
        logger($"[FINALIZE] -> New bounds: {newMesh.bounds}");

        logger("[FINALIZE] Determining asset save path...");
        string path = AssetDatabase.GetAssetPath(originalMesh);
        path = string.IsNullOrEmpty(path)
            ? "Assets/" + originalMesh.name + "_cleaned.asset"
            : Path.GetDirectoryName(path) + "/" + Path.GetFileNameWithoutExtension(originalMesh.name) + "_cleaned.asset";
        string uniquePath = AssetDatabase.GenerateUniqueAssetPath(path);
        logger($"[FINALIZE] -> Path set to: {uniquePath}");

        logger("[FINALIZE] Creating asset on disk...");
        AssetDatabase.CreateAsset(newMesh, uniquePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        logger("[FINALIZE] -> Asset saved and database refreshed.");

        logger($"[FINALIZE] Assigning new mesh and {newMaterials.Count} materials back to SkinnedMeshRenderer...");
        skinnedMeshRenderer.sharedMesh = newMesh;
        skinnedMeshRenderer.sharedMaterials = newMaterials.ToArray();
        logger("[FINALIZE] -> Assignment complete.");

        int removedCount = originalVertices.Length - newVertices.Count;
        string logMessage = $"Successfully removed {removedCount} vertices. New mesh asset created at: {uniquePath}";
        EditorUtility.DisplayDialog("Mesh Cleaned Successfully", logMessage, "OK");
        logger(logMessage);
    }
}

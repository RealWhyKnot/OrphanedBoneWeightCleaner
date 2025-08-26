using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class OrphanedBoneWeightCleaner
{
    [MenuItem("Tools/WhyKnot/Clean Mesh By Orphaned Bone Weights")]
    private static void CleanMesh()
    {
        Debug.Log("========================================================\n" +
                  "--- SCRIPT STARTED: Clean Mesh By Orphaned Bone Weights ---\n" +
                  "========================================================");

        Debug.Log("[SETUP] Getting selected object from Unity Editor.");
        GameObject selectedObject = Selection.activeGameObject;

        if (selectedObject == null)
        {
            EditorUtility.DisplayDialog("No Object Selected", "Please select a GameObject with a SkinnedMeshRenderer.", "OK");
            Debug.LogError("[SETUP] FAILED: No GameObject was selected. Aborting script.");
            return;
        }
        Debug.Log($"[SETUP] Selected object: '{selectedObject.name}'.");

        SkinnedMeshRenderer skinnedMeshRenderer = selectedObject.GetComponent<SkinnedMeshRenderer>();
        if (skinnedMeshRenderer == null)
        {
            EditorUtility.DisplayDialog("No SkinnedMeshRenderer Found", "The selected object does not have a SkinnedMeshRenderer component.", "OK");
            Debug.LogError($"[SETUP] FAILED: No SkinnedMeshRenderer component found on '{selectedObject.name}'. Aborting script.");
            return;
        }
        Debug.Log($"[SETUP] Found SkinnedMeshRenderer on '{skinnedMeshRenderer.name}'.");

        Mesh originalMesh = skinnedMeshRenderer.sharedMesh;
        if (originalMesh == null)
        {
            EditorUtility.DisplayDialog("No Mesh Found", "The SkinnedMeshRenderer does not have a mesh assigned.", "OK");
            Debug.LogError("[SETUP] FAILED: The SkinnedMeshRenderer has no 'sharedMesh' assigned. Aborting script.");
            return;
        }
        Debug.Log($"[SETUP] Found original mesh: '{originalMesh.name}'.");
        Debug.Log($"[SETUP] Original Mesh Stats | Vertices: {originalMesh.vertexCount}, Sub-meshes: {originalMesh.subMeshCount}, Blendshapes: {originalMesh.blendShapeCount}, Materials: {skinnedMeshRenderer.sharedMaterials.Length}");


        Debug.Log("[DATA] Reading all data arrays from the original mesh...");
        Vector3[] originalVertices = originalMesh.vertices;
        Debug.Log($"[DATA] -> Read {originalVertices.Length} vertices.");
        BoneWeight[] originalBoneWeights = originalMesh.boneWeights;
        Debug.Log($"[DATA] -> Read {originalBoneWeights.Length} bone weights.");
        Transform[] bones = skinnedMeshRenderer.bones;
        Debug.Log($"[DATA] -> Read {bones.Length} bones from SkinnedMeshRenderer.");

        bool hasNormals = originalMesh.normals.Length > 0;
        bool hasTangents = originalMesh.tangents.Length > 0;
        bool hasUVs = originalMesh.uv.Length > 0;
        bool hasColors = originalMesh.colors.Length > 0;
        Debug.Log($"[DATA] Mesh attribute check: hasNormals={hasNormals}, hasTangents={hasTangents}, hasUVs={hasUVs}, hasColors={hasColors}");

        Vector3[] originalNormals = hasNormals ? originalMesh.normals : null;
        Vector4[] originalTangents = hasTangents ? originalMesh.tangents : null;
        Vector2[] originalUVs = hasUVs ? originalMesh.uv : null;
        Color[] originalColors = hasColors ? originalMesh.colors : null;

        Debug.Log("[ANALYSIS] Starting analysis of bone weights for each vertex...");
        List<int> verticesToKeep = new List<int>();
        List<int> verticesToRemove = new List<int>();
        int boneCount = bones.Length;

        for (int i = 0; i < originalBoneWeights.Length; i++)
        {
            if (i > 0 && i % 25000 == 0) Debug.Log($"[ANALYSIS] ...processed {i} / {originalBoneWeights.Length} vertices...");

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
                if (verticesToRemove.Count < 20) // Log first 20 offenders
                {
                    Debug.LogWarning($"[ANALYSIS] Found orphaned vertex at index {i}. Reason: {reason} is out of bounds (bone count is {boneCount}) or bone transform is null.");
                }
            }
            else
            {
                verticesToKeep.Add(i);
            }
        }
        Debug.Log($"[ANALYSIS] Vertex analysis complete. Vertices to keep: {verticesToKeep.Count}. Vertices to remove: {verticesToRemove.Count}.");

        if (verticesToKeep.Count == originalVertices.Length)
        {
            EditorUtility.DisplayDialog("No Orphaned Weights Found", "All vertices are weighted to existing bones. No changes were made.", "OK");
            Debug.Log("[ANALYSIS] No orphaned bone weights found. Aborting script as no changes are needed.");
            return;
        }
        if (verticesToKeep.Count == 0)
        {
            EditorUtility.DisplayDialog("No Validly Weighted Vertices", "No vertices are weighted to existing bones. Cannot create a new mesh.", "OK");
            Debug.LogError("[ANALYSIS] FAILED: No validly weighted vertices found. Aborting script.");
            return;
        }

        Debug.Log("[MAPPING] Creating old-to-new vertex index map...");
        int[] oldToNewVertexIndexMap = new int[originalVertices.Length];
        for (int i = 0; i < oldToNewVertexIndexMap.Length; i++) oldToNewVertexIndexMap[i] = -1; // Initialize with -1
        for (int i = 0; i < verticesToKeep.Count; i++)
        {
            oldToNewVertexIndexMap[verticesToKeep[i]] = i;
        }
        Debug.Log("[MAPPING] Index map created.");

        Debug.Log("[MAPPING] Populating new data lists for kept vertices...");
        List<Vector3> newVertices = new List<Vector3>(verticesToKeep.Count);
        List<BoneWeight> newBoneWeights = new List<BoneWeight>(verticesToKeep.Count);
        List<Vector3> newNormals = hasNormals ? new List<Vector3>(verticesToKeep.Count) : null;
        List<Vector4> newTangents = hasTangents ? new List<Vector4>(verticesToKeep.Count) : null;
        List<Vector2> newUVs = hasUVs ? new List<Vector2>(verticesToKeep.Count) : null;
        List<Color> newColors = hasColors ? new List<Color>(verticesToKeep.Count) : null;

        foreach (int oldIndex in verticesToKeep)
        {
            newVertices.Add(originalVertices[oldIndex]);
            newBoneWeights.Add(originalBoneWeights[oldIndex]);
            if (hasNormals) newNormals.Add(originalNormals[oldIndex]);
            if (hasTangents) newTangents.Add(originalTangents[oldIndex]);
            if (hasUVs) newUVs.Add(originalUVs[oldIndex]);
            if (hasColors) newColors.Add(originalColors[oldIndex]);
        }
        Debug.Log("[MAPPING] New data lists populated.");
        Debug.Log($"[MAPPING] New list counts | Vertices: {newVertices.Count}, BoneWeights: {newBoneWeights.Count}, Normals: {newNormals?.Count ?? 0}");

        Debug.Log("--- [SANITY CHECK PRE-BUILD] ---");
        int[] checkIndices = { 0, verticesToKeep.Count / 4, verticesToKeep.Count / 2, (verticesToKeep.Count / 4) * 3, verticesToKeep.Count - 1 };
        foreach (int newIndex in checkIndices)
        {
            if (newIndex < 0 || newIndex >= verticesToKeep.Count) continue;
            int oldIndex = verticesToKeep[newIndex];
            var sb = new StringBuilder();
            sb.AppendLine($"--- Checking New Index {newIndex} (from Old Index {oldIndex}) ---");
            sb.AppendLine($"  POS: Old={originalVertices[oldIndex]} | New={newVertices[newIndex]} | Match={originalVertices[oldIndex] == newVertices[newIndex]}");
            if(hasNormals) sb.AppendLine($"  NRM: Old={originalNormals[oldIndex]} | New={newNormals[newIndex]} | Match={originalNormals[oldIndex] == newNormals[newIndex]}");
            sb.AppendLine($"  B_W: Old=(b{originalBoneWeights[oldIndex].boneIndex0}, w{originalBoneWeights[oldIndex].weight0:F2}) | New=(b{newBoneWeights[newIndex].boneIndex0}, w{newBoneWeights[newIndex].weight0:F2}) | Match={originalBoneWeights[oldIndex].Equals(newBoneWeights[newIndex])}");
            Debug.Log(sb.ToString());
        }
        Debug.Log("--- [SANITY CHECK PRE-BUILD COMPLETE] ---");


        Debug.Log("[TRIANGLES] Rebuilding triangles for each sub-mesh...");
        List<List<int>> newSubmeshTriangles = new List<List<int>>();
        List<Material> newMaterials = new List<Material>();
        Material[] originalMaterials = skinnedMeshRenderer.sharedMaterials;
        HashSet<int> verticesToKeepSet = new HashSet<int>(verticesToKeep);

        for (int i = 0; i < originalMesh.subMeshCount; i++)
        {
            Debug.Log($"[TRIANGLES] Processing Sub-mesh {i}/{originalMesh.subMeshCount - 1}...");
            int[] originalSubTriangles = originalMesh.GetTriangles(i);
            Debug.Log($"[TRIANGLES] -> Sub-mesh {i} has {originalSubTriangles.Length / 3} triangles.");
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

            if (newTrianglesForSubmesh.Count > 0)
            {
                newSubmeshTriangles.Add(newTrianglesForSubmesh);
                Material mat = (i < originalMaterials.Length) ? originalMaterials[i] : null;
                newMaterials.Add(mat);
                Debug.Log($"[TRIANGLES] -> Sub-mesh {i} KEPT. New triangle count: {newTrianglesForSubmesh.Count / 3}. Associated material: '{(mat != null ? mat.name : "NULL")}'");
            }
            else
            {
                Debug.LogWarning($"[TRIANGLES] -> Sub-mesh {i} DISCARDED as it has no valid triangles left.");
            }
        }
        Debug.Log("[TRIANGLES] Triangle rebuilding complete.");

        Debug.Log("[CREATION] Creating new Mesh object...");
        Mesh newMesh = new Mesh();
        newMesh.name = originalMesh.name + "_cleaned";
        Debug.Log($"[CREATION] -> Name set to '{newMesh.name}'.");

        if (newVertices.Count > 65535)
        {
            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            Debug.Log("[CREATION] -> Vertex count > 65535. Set index format to UInt32.");
        }
        else
        {
            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            Debug.Log("[CREATION] -> Vertex count <= 65535. Set index format to UInt16.");
        }

        Debug.Log($"[CREATION] Assigning {newVertices.Count} vertices...");
        newMesh.vertices = newVertices.ToArray();
        Debug.Log($"[CREATION] Assigning {newBoneWeights.Count} bone weights...");
        newMesh.boneWeights = newBoneWeights.ToArray();
        if (hasNormals) { Debug.Log($"[CREATION] Assigning {newNormals.Count} normals..."); newMesh.normals = newNormals.ToArray(); }
        if (hasTangents) { Debug.Log($"[CREATION] Assigning {newTangents.Count} tangents..."); newMesh.tangents = newTangents.ToArray(); }
        if (hasUVs) { Debug.Log($"[CREATION] Assigning {newUVs.Count} UVs..."); newMesh.uv = newUVs.ToArray(); }
        if (hasColors) { Debug.Log($"[CREATION] Assigning {newColors.Count} colors..."); newMesh.colors = newColors.ToArray(); }

        Debug.Log($"[CREATION] Assigning triangles for {newSubmeshTriangles.Count} sub-meshes...");
        newMesh.subMeshCount = newSubmeshTriangles.Count;
        for (int i = 0; i < newSubmeshTriangles.Count; i++)
        {
            newMesh.SetTriangles(newSubmeshTriangles[i].ToArray(), i);
            Debug.Log($"[CREATION] -> Assigned {newSubmeshTriangles[i].Count / 3} triangles to sub-mesh {i}.");
        }
        Debug.Log("[CREATION] Base mesh data assigned.");


        Debug.Log("[BLENDSHAPES] Rebuilding blendshapes...");
        newMesh.ClearBlendShapes();
        int blendShapeCount = originalMesh.blendShapeCount;
        if (blendShapeCount > 0)
        {
            for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
            {
                string shapeName = originalMesh.GetBlendShapeName(shapeIndex);
                int frameCount = originalMesh.GetBlendShapeFrameCount(shapeIndex);
                Debug.Log($"[BLENDSHAPES] Processing '{shapeName}' ({shapeIndex + 1}/{blendShapeCount}), which has {frameCount} frame(s).");

                Vector3[] deltaVertices = new Vector3[originalVertices.Length];
                Vector3[] deltaNormals = hasNormals ? new Vector3[originalVertices.Length] : null;
                Vector3[] deltaTangents = hasTangents ? new Vector3[originalVertices.Length] : null;

                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    float frameWeight = originalMesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex);
                    Debug.Log($"[BLENDSHAPES] -> Processing frame {frameIndex} with weight {frameWeight}.");
                    originalMesh.GetBlendShapeFrameVertices(shapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);
                    Debug.Log($"[BLENDSHAPES] -> -> Got original delta data.");

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
                    Debug.Log($"[BLENDSHAPES] -> -> Mapped old deltas to new vertex list.");

                    foreach (int newIndex in checkIndices)
                    {
                        if (newIndex < 0 || newIndex >= verticesToKeep.Count) continue;
                        int oldIndex = verticesToKeep[newIndex];
                        if (deltaVertices[oldIndex] != Vector3.zero)
                        {
                            bool match = deltaVertices[oldIndex] == newDeltaVertices[newIndex];
                            Debug.Log($"[BLENDSHAPES] SANITY CHECK ('{shapeName}') | New Idx: {newIndex} (Old: {oldIndex}) | Match: {match} | Delta: {newDeltaVertices[newIndex]}");
                        }
                    }

                    newMesh.AddBlendShapeFrame(shapeName, frameWeight, newDeltaVertices, newDeltaNormals, newDeltaTangents);
                    Debug.Log($"[BLENDSHAPES] -> -> Added frame to new mesh.");
                }
            }
            Debug.Log("[BLENDSHAPES] Blendshape processing complete.");
        }
        else
        {
            Debug.Log("[BLENDSHAPES] No blendshapes found on original mesh.");
        }

        Debug.Log("[FINALIZE] Copying bindposes...");
        newMesh.bindposes = originalMesh.bindposes;
        Debug.Log("[FINALIZE] Recalculating bounds...");
        newMesh.RecalculateBounds();
        Debug.Log($"[FINALIZE] -> New bounds: {newMesh.bounds}");

        Debug.Log("[FINALIZE] Determining asset save path...");
        string path = AssetDatabase.GetAssetPath(originalMesh);
        path = string.IsNullOrEmpty(path)
            ? "Assets/" + originalMesh.name + "_cleaned.asset"
            : Path.GetDirectoryName(path) + "/" + Path.GetFileNameWithoutExtension(originalMesh.name) + "_cleaned.asset";
        string uniquePath = AssetDatabase.GenerateUniqueAssetPath(path);
        Debug.Log($"[FINALIZE] -> Path set to: {uniquePath}");

        Debug.Log("[FINALIZE] Creating asset on disk...");
        AssetDatabase.CreateAsset(newMesh, uniquePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[FINALIZE] -> Asset saved.");

        Debug.Log($"[FINALIZE] Assigning new mesh and {newMaterials.Count} materials back to SkinnedMeshRenderer...");
        skinnedMeshRenderer.sharedMesh = newMesh;
        skinnedMeshRenderer.sharedMaterials = newMaterials.ToArray();
        Debug.Log("[FINALIZE] -> Assignment complete.");

        int removedCount = originalVertices.Length - newVertices.Count;
        string logMessage = $"Successfully removed {removedCount} vertices. New mesh asset created at: {uniquePath}";
        EditorUtility.DisplayDialog("Mesh Cleaned Successfully", logMessage, "OK");

        Debug.Log("========================================================\n" +
                  $"--- SCRIPT FINISHED --- Removed {removedCount} vertices. ---\n" +
                  "========================================================");
    }
}

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class OrphanedBoneWeightCleaner
{
    [MenuItem("Tools/WhyKnot/Clean Mesh By Orphaned Bone Weights")]
    private static void CleanMesh()
    {
        // Get the currently selected GameObject in the editor.
        GameObject selectedObject = Selection.activeGameObject;

        if (selectedObject == null)
        {
            EditorUtility.DisplayDialog("No Object Selected", "Please select a GameObject with a SkinnedMeshRenderer.", "OK");
            return;
        }

        SkinnedMeshRenderer skinnedMeshRenderer = selectedObject.GetComponent<SkinnedMeshRenderer>();
        if (skinnedMeshRenderer == null)
        {
            EditorUtility.DisplayDialog("No SkinnedMeshRenderer Found", "The selected object does not have a SkinnedMeshRenderer component.", "OK");
            return;
        }

        Mesh originalMesh = skinnedMeshRenderer.sharedMesh;
        if (originalMesh == null)
        {
            EditorUtility.DisplayDialog("No Mesh Found", "The SkinnedMeshRenderer does not have a mesh assigned.", "OK");
            return;
        }

        // Get all data from the original mesh and renderer.
        Vector3[] originalVertices = originalMesh.vertices;
        BoneWeight[] originalBoneWeights = originalMesh.boneWeights;
        int[] originalTriangles = originalMesh.triangles;
        Vector3[] originalNormals = originalMesh.normals;
        Vector2[] originalUVs = originalMesh.uv;
        Transform[] bones = skinnedMeshRenderer.bones;
        int boneCount = bones.Length;

        // A list to store the indices of the vertices we want to keep.
        List<int> verticesToKeep = new List<int>();
        for (int i = 0; i < originalBoneWeights.Length; i++)
        {
            BoneWeight bw = originalBoneWeights[i];
            bool hasInvalidBone = false;

            // Check if any bone index is out of bounds OR if the bone at that index is null.
            if (bw.weight0 > 0 && (bw.boneIndex0 >= boneCount || bones[bw.boneIndex0] == null)) hasInvalidBone = true;
            if (bw.weight1 > 0 && (bw.boneIndex1 >= boneCount || bones[bw.boneIndex1] == null)) hasInvalidBone = true;
            if (bw.weight2 > 0 && (bw.boneIndex2 >= boneCount || bones[bw.boneIndex2] == null)) hasInvalidBone = true;
            if (bw.weight3 > 0 && (bw.boneIndex3 >= boneCount || bones[bw.boneIndex3] == null)) hasInvalidBone = true;
            
            // Keep the vertex only if all its weights point to valid, non-null bones.
            if (!hasInvalidBone)
            {
                verticesToKeep.Add(i);
            }
        }

        if (verticesToKeep.Count == originalVertices.Length)
        {
            EditorUtility.DisplayDialog("No Orphaned Weights Found", "All vertices in this mesh are weighted to existing bones. No changes were made.", "OK");
            return;
        }

        if (verticesToKeep.Count == 0)
        {
            EditorUtility.DisplayDialog("No Validly Weighted Vertices", "No vertices in this mesh are weighted to existing bones. Cannot create a new mesh.", "OK");
            return;
        }

        // Create a map from the old vertex index to the new vertex index.
        int[] oldToNewVertexIndexMap = new int[originalVertices.Length];
        for (int i = 0; i < verticesToKeep.Count; i++)
        {
            oldToNewVertexIndexMap[verticesToKeep[i]] = i;
        }

        // Create new data arrays for the cleaned mesh.
        List<Vector3> newVertices = new List<Vector3>();
        List<BoneWeight> newBoneWeights = new List<BoneWeight>();
        List<Vector3> newNormals = new List<Vector3>();
        List<Vector2> newUVs = new List<Vector2>();

        foreach (int oldIndex in verticesToKeep)
        {
            newVertices.Add(originalVertices[oldIndex]);
            newBoneWeights.Add(originalBoneWeights[oldIndex]);
            if(originalNormals.Length > 0) newNormals.Add(originalNormals[oldIndex]);
            if(originalUVs.Length > 0) newUVs.Add(originalUVs[oldIndex]);
        }

        // Rebuild the triangles array.
        List<int> newTriangles = new List<int>();
        for (int i = 0; i < originalTriangles.Length; i += 3)
        {
            int oldV1 = originalTriangles[i];
            int oldV2 = originalTriangles[i + 1];
            int oldV3 = originalTriangles[i + 2];

            // If all three vertices of a triangle are being kept, add it to the new mesh.
            if (verticesToKeep.Contains(oldV1) && verticesToKeep.Contains(oldV2) && verticesToKeep.Contains(oldV3))
            {
                newTriangles.Add(oldToNewVertexIndexMap[oldV1]);
                newTriangles.Add(oldToNewVertexIndexMap[oldV2]);
                newTriangles.Add(oldToNewVertexIndexMap[oldV3]);
            }
        }

        // Create and assign the new mesh.
        Mesh newMesh = new Mesh();
        newMesh.name = originalMesh.name + "_cleaned";
        newMesh.vertices = newVertices.ToArray();
        newMesh.triangles = newTriangles.ToArray();
        newMesh.boneWeights = newBoneWeights.ToArray();
        if(newNormals.Count > 0) newMesh.normals = newNormals.ToArray();
        if(newUVs.Count > 0) newMesh.uv = newUVs.ToArray();
        newMesh.bindposes = originalMesh.bindposes;

        newMesh.RecalculateBounds();
        
        skinnedMeshRenderer.sharedMesh = newMesh;

        int removedCount = originalVertices.Length - newVertices.Count;
        Debug.Log($"Successfully cleaned mesh. Removed {removedCount} vertices with orphaned bone weights.");
        EditorUtility.DisplayDialog("Mesh Cleaned", $"Successfully removed {removedCount} vertices with orphaned bone weights.", "OK");
    }
}

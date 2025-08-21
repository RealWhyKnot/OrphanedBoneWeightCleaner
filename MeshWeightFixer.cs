using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO; 

public class OrphanedBoneWeightCleaner
{
    [MenuItem("Tools/WhyKnot/Clean Mesh By Orphaned Bone Weights")]
    private static void CleanMesh()
    {
        
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

        Vector3[] originalVertices = originalMesh.vertices;
        BoneWeight[] originalBoneWeights = originalMesh.boneWeights;
        int[] originalTriangles = originalMesh.triangles;
        Transform[] bones = skinnedMeshRenderer.bones;
        int boneCount = bones.Length;
        
        bool hasNormals = originalMesh.normals.Length > 0;
        bool hasTangents = originalMesh.tangents.Length > 0;
        bool hasUVs = originalMesh.uv.Length > 0;
        bool hasColors = originalMesh.colors.Length > 0;
        
        Vector3[] originalNormals = hasNormals ? originalMesh.normals : null;
        Vector4[] originalTangents = hasTangents ? originalMesh.tangents : null;
        Vector2[] originalUVs = hasUVs ? originalMesh.uv : null;
        Color[] originalColors = hasColors ? originalMesh.colors : null;

        List<int> verticesToKeep = new List<int>();
        for (int i = 0; i < originalBoneWeights.Length; i++)
        {
            BoneWeight bw = originalBoneWeights[i];
            bool hasInvalidBone = false;

            if (bw.weight0 > 0 && (bw.boneIndex0 >= boneCount || bones[bw.boneIndex0] == null)) hasInvalidBone = true;
            if (bw.weight1 > 0 && (bw.boneIndex1 >= boneCount || bones[bw.boneIndex1] == null)) hasInvalidBone = true;
            if (bw.weight2 > 0 && (bw.boneIndex2 >= boneCount || bones[bw.boneIndex2] == null)) hasInvalidBone = true;
            if (bw.weight3 > 0 && (bw.boneIndex3 >= boneCount || bones[bw.boneIndex3] == null)) hasInvalidBone = true;
            
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

        
        int[] oldToNewVertexIndexMap = new int[originalVertices.Length];
        for (int i = 0; i < verticesToKeep.Count; i++)
        {
            oldToNewVertexIndexMap[verticesToKeep[i]] = i;
        }
        
        List<Vector3> newVertices = new List<Vector3>();
        List<BoneWeight> newBoneWeights = new List<BoneWeight>();
        List<Vector3> newNormals = new List<Vector3>();
        List<Vector4> newTangents = new List<Vector4>();
        List<Vector2> newUVs = new List<Vector2>();
        List<Color> newColors = new List<Color>();

        foreach (int oldIndex in verticesToKeep)
        {
            newVertices.Add(originalVertices[oldIndex]);
            newBoneWeights.Add(originalBoneWeights[oldIndex]);
            if (hasNormals) newNormals.Add(originalNormals[oldIndex]);
            if (hasTangents) newTangents.Add(originalTangents[oldIndex]);
            if (hasUVs) newUVs.Add(originalUVs[oldIndex]);
            if (hasColors) newColors.Add(originalColors[oldIndex]);
        }

        List<int> newTriangles = new List<int>();
        
        HashSet<int> verticesToKeepSet = new HashSet<int>(verticesToKeep);

        for (int i = 0; i < originalTriangles.Length; i += 3)
        {
            int oldV1 = originalTriangles[i];
            int oldV2 = originalTriangles[i + 1];
            int oldV3 = originalTriangles[i + 2];

            if (verticesToKeepSet.Contains(oldV1) && verticesToKeepSet.Contains(oldV2) && verticesToKeepSet.Contains(oldV3))
            {
                newTriangles.Add(oldToNewVertexIndexMap[oldV1]);
                newTriangles.Add(oldToNewVertexIndexMap[oldV2]);
                newTriangles.Add(oldToNewVertexIndexMap[oldV3]);
            }
        }

        Mesh newMesh = new Mesh();
        newMesh.name = originalMesh.name + "_cleaned";
        newMesh.vertices = newVertices.ToArray();
        newMesh.triangles = newTriangles.ToArray();
        newMesh.boneWeights = newBoneWeights.ToArray();
        
        if (hasNormals) newMesh.normals = newNormals.ToArray();
        if (hasTangents) newMesh.tangents = newTangents.ToArray();
        if (hasUVs) newMesh.uv = newUVs.ToArray();
        if (hasColors) newMesh.colors = newColors.ToArray();
        
        newMesh.bindposes = originalMesh.bindposes;
        newMesh.RecalculateBounds();
        
        string path = AssetDatabase.GetAssetPath(originalMesh);
        if (string.IsNullOrEmpty(path))
        {
            path = "Assets/" + originalMesh.name + "_cleaned.asset";
        }
        else
        {
            string directory = Path.GetDirectoryName(path);
            string filename = Path.GetFileNameWithoutExtension(originalMesh.name);
            path = directory + "/" + filename + "_cleaned.asset";
        }

        AssetDatabase.CreateAsset(newMesh, AssetDatabase.GenerateUniqueAssetPath(path));
        AssetDatabase.SaveAssets();

        skinnedMeshRenderer.sharedMesh = newMesh;

        int removedCount = originalVertices.Length - newVertices.Count;
        string logMessage = $"Successfully removed {removedCount} vertices. New mesh asset created at: {path}";
        Debug.Log(logMessage, selectedObject);
        EditorUtility.DisplayDialog("Mesh Cleaned", logMessage, "OK");
    }
}

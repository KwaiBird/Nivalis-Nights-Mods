using MagicaCloth;
using Nivalis;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NivalisNights.FlatNivalisTimes;

internal static class PaperStates
{
    private static readonly Dictionary<IntPtr, PaperState> States = new();
    private static readonly List<IntPtr> Dead = new();
    internal static bool HasOwnedResources => States.Values.Any(state => state.OwnsResources);
    internal static bool Alive(Object? value) => value != null && value;
    internal static bool IsApplied(Newspaper paper) => States.TryGetValue(paper.Pointer, out var state) && state.Applied;
    internal static void RetryRequested()
    {
        ReadingGrip.AllowRetry();
        foreach (var state in States.Values) state.AllowRetry();
    }

    internal static void UpdateAnimation(Newspaper paper)
    {
        if (!Alive(paper) || paper.isPoster || paper.AnimationProgress <= 0) return;
        if (!States.TryGetValue(paper.Pointer, out var state))
        {
            if (!AccessibilitySettings.Enabled) return;
            state = new PaperState(paper);
            States.Add(paper.Pointer, state);
        }
        if (state.Applied) RemoveIdleNoise(paper);
    }

    internal static void CompleteBoundary(MagicaPhysicsManager manager)
    {
        Dead.Clear();
        foreach (var (key, state) in States)
        {
            try
            {
                // A completed boundary on a replacement manager does not authorize
                // joining or releasing the previous manager's consumers.
                if (!state.CheckBoundaryOwner(manager)) continue;
                if (!Alive(state.Paper))
                {
                    ReadingGrip.Release(state.Paper);
                    if (state.ReleaseDestroyedOwner(manager)) Dead.Add(key);
                }
                else if (!state.Failed)
                {
                    if (AccessibilitySettings.Enabled && !state.Applied) state.TryApply(manager);
                    else if (!AccessibilitySettings.Enabled && state.Applied) state.TryRestore(manager);
                }
                }
            catch (Exception error) { Plugin.ReportOnce("paper boundary retained", error); }
        }
        foreach (var key in Dead) States.Remove(key);
    }

    private static void RemoveIdleNoise(Newspaper paper)
    {
        // In this build UpdateAnimation adds these WORLD-space offsets after its
        // original camera/equip interpolation, then multiplies baseRotation * noise.
        // Time.unscaledTime is stable within the frame, including this postfix.
        float time = Time.unscaledTime;
        var offset = new Vector3(
            (Mathf.PerlinNoise(time * .54f, 5.324f) - .5f) * .003f,
            (Mathf.PerlinNoise(time * .73f, 7.665f) - .5f) * .003f,
            (Mathf.PerlinNoise(time * .83f, 2.234f) - .5f) * .003f);
        var angles = new Vector3(
            (Mathf.PerlinNoise(time * .055f, 7.324f) - .5f) * 5f,
            (Mathf.PerlinNoise(time * .093f, 8.636f) - .5f) * 5f,
            (Mathf.PerlinNoise(time * .062f, 3.828f) - .5f) * 5f);
        var transform = paper.meshRenderer.transform;
        var rotation = transform.rotation * Quaternion.Inverse(Quaternion.Euler(angles));
        transform.SetPositionAndRotation(transform.position - offset, rotation);
    }
}

internal sealed class PaperState
{
    internal readonly Newspaper Paper;
    internal bool Applied { get; private set; }
    internal bool Failed { get; private set; }
    internal bool OwnsResources => flat != null || captured;
    private Mesh? flat, original, previousMesh;
    private MeshFilter? filter;
    private MagicaMeshCloth? cloth;
    private MagicaVirtualDeformer? virtualDeformer;
    private MagicaRenderDeformer? renderDeformer;
    private bool captured, clothEnable, virtualEnable, renderEnable;
    private MagicaPhysicsManager? boundaryOwner;
    private PhysicsManagerCompute? computeOwner;

    internal bool CheckBoundaryOwner(MagicaPhysicsManager manager)
    {
        if (!captured) return true;
        try
        {
            if (!PaperStates.Alive(boundaryOwner) || manager.Pointer != boundaryOwner!.Pointer
                || manager.Compute == null || manager.Compute.Pointer != computeOwner?.Pointer)
                throw new InvalidOperationException("Paper cloth manager changed; retained snapshot cannot be restored by another manager.");
            if (PaperStates.Alive(Paper) && (!PaperStates.Alive(cloth) || !PaperStates.Alive(virtualDeformer)
                || !PaperStates.Alive(renderDeformer) || !PaperStates.Alive(filter)
                || Paper.meshRenderer.GetComponent<MeshFilter>()?.Pointer != filter!.Pointer
                || Paper.meshRenderer.GetComponent<MagicaMeshCloth>()?.Pointer != cloth!.Pointer
                || Paper.meshRenderer.GetComponent<MagicaVirtualDeformer>()?.Pointer != virtualDeformer!.Pointer
                || Paper.meshRenderer.GetComponent<MagicaRenderDeformer>()?.Pointer != renderDeformer!.Pointer
                || cloth!.virtualDeformer?.Pointer != virtualDeformer!.Pointer
                || renderDeformer!.Deformer.meshFilter?.Pointer != filter.Pointer))
                throw new InvalidOperationException("Paper component owner changed; retained snapshot may not overwrite replacements.");
            return true;
        }
        catch (Exception error) { Failed = true; Plugin.ReportOnce("paper owner retained", error); return false; }
    }

    internal PaperState(Newspaper paper) => Paper = paper;
    internal void AllowRetry() => Failed = false;

    private static bool Ready(CoreComponent component)
    {
        var status = component.Status;
        if (status == null) return false;
        if (status.IsInitError || status.IsDispose)
            throw new InvalidOperationException("Paper deformation initialization failed or the component was disposed.");
        return status.IsInitSuccess;
    }

    internal void TryApply(MagicaPhysicsManager manager)
    {
        try
        {
            if (captured) throw new InvalidOperationException("An unresolved paper snapshot requires restart before reapplying.");
            var renderer = Paper.meshRenderer;
            if (!PaperStates.Alive(renderer)) return;
            // All four components are on the renderer's own GameObject in Original.
            // GetComponentInChildren on the newspaper root is not needed.
            cloth ??= renderer.GetComponent<MagicaMeshCloth>();
            virtualDeformer ??= renderer.GetComponent<MagicaVirtualDeformer>();
            renderDeformer ??= renderer.GetComponent<MagicaRenderDeformer>();
            filter ??= renderer.GetComponent<MeshFilter>();
            if (!PaperStates.Alive(cloth) || !PaperStates.Alive(virtualDeformer) ||
                !PaperStates.Alive(renderDeformer) || !PaperStates.Alive(filter))
                throw new InvalidOperationException("Original paper component chain is missing.");
            if (!Ready(cloth!) || !Ready(virtualDeformer!) || !Ready(renderDeformer!)) return;
            var renderList = virtualDeformer!.Deformer.renderDeformerList;
            if (cloth!.virtualDeformer.Pointer != virtualDeformer!.Pointer ||
                renderList == null || renderList.Count != 1 || renderList[0].Pointer != renderDeformer!.Pointer ||
                renderDeformer.Deformer.meshFilter.Pointer != filter!.Pointer)
                throw new InvalidOperationException("Paper deformer references do not match its renderer.");
            var deformer = renderDeformer.Deformer;
            original = deformer.SharedMesh;
            if (!PaperStates.Alive(original) || !original!.isReadable || original.vertexCount != 81)
                throw new InvalidOperationException("Paper source mesh does not match the investigated 81-vertex mesh.");
            if (filter!.sharedMesh != original && filter.sharedMesh != deformer.cloneMesh)
                throw new InvalidOperationException("Another owner replaced the paper mesh; application may not overwrite it.");
            var vertices = original.vertices;
            foreach (var vertex in vertices)
                if (Math.Abs(vertex.x) > .445f || Math.Abs(vertex.y) > .251f || Math.Abs(vertex.z) > .017f)
                    throw new InvalidOperationException("Paper source geometry is outside the investigated bounds.");
            flat = Object.Instantiate(original);
            flat.name = "Flat Nivalis Times";
            for (int i = 0; i < vertices.Length; i++)
            {
                var vertex = vertices[i];
                // Least-squares plane of Original's 81 vertices. XY/UV/index data stay intact.
                vertex.z = -.00019299232f * vertex.x + .00449846246f * vertex.y + .00033233867f;
                vertices[i] = vertex;
            }
            flat.vertices = vertices;
            flat.RecalculateNormals();
            flat.RecalculateTangents();
            flat.RecalculateBounds();
            clothEnable = cloth.Status.userEnable;
            virtualEnable = virtualDeformer.Status.userEnable;
            renderEnable = renderDeformer.Status.userEnable;
            previousMesh = filter.sharedMesh;
            boundaryOwner = manager;
            computeOwner = manager.Compute ?? throw new InvalidOperationException("Paper cloth compute owner is missing.");
            captured = true;
            // CompleteJob only joins the existing handle; unlike UpdateCompleteSimulation,
            // it does not set runMasterJob. This is a one-time operation for this owner.
            manager.Compute.CompleteJob();
            cloth.SetUserEnable(false);
            virtualDeformer.SetUserEnable(false);
            renderDeformer.SetUserEnable(false);
            // bufferIndex follows the original manager's 0/1 choice. This settles oldUse
            // and clears write flags before assigning our mesh, outside collection iteration.
            deformer.MeshCalculation(manager.useDelay ? 1 : 0);
            if (cloth.Status.IsActive || virtualDeformer.Status.IsActive || renderDeformer.Status.IsActive ||
                deformer.oldUse || deformer.IsWriteMeshPosition || deformer.IsWriteMeshBoneWeight ||
                deformer.IsFasterWriteUpdate || deformer.IsWriteMeshNormal || deformer.IsWriteMeshTangent ||
                filter.sharedMesh.Pointer != original.Pointer)
                throw new InvalidOperationException("Paper deformation did not reach the inactive original-mesh state.");
            filter.sharedMesh = flat;
            Applied = true;
            Plugin.Logger.LogInfo($"Flat paper applied to owner {Paper.GetInstanceID()}: 81 vertices; hands and original shared mesh retained. RuntimeAccepted=false.");
        }
        catch (Exception error)
        {
            Failed = true;
            try
            {
                if (captured) manager.Compute.CompleteJob();
                RestoreAtBoundary(manager);
            }
            catch (Exception restoreError) { Plugin.ReportOnce("restore requires restart", restoreError); }
            AccessibilitySettings.Reject(false, error);
            Plugin.ReportOnce("paper application rejected", error);
        }
    }

    internal void TryRestore(MagicaPhysicsManager manager)
    {
        try
        {
            manager.Compute.CompleteJob();
            ReadingGrip.Release(Paper);
            RestoreAtBoundary(manager);
            Plugin.Logger.LogInfo("Original newspaper deformation and grip restored. RuntimeAccepted=false.");
        }
        catch (Exception error)
        {
            Failed = true;
            // Keep the captured original snapshot until restoration has fully succeeded.
            // If reactivation failed partway, return this owner to its last working flat mode.
            try
            {
                manager.Compute.CompleteJob();
                var current = filter!.sharedMesh;
                if (current != flat && current != original && current != previousMesh)
                    throw new InvalidOperationException("Another owner replaced the paper mesh; flat rollback may not overwrite it.");
                cloth!.SetUserEnable(false);
                virtualDeformer!.SetUserEnable(false);
                renderDeformer!.SetUserEnable(false);
                renderDeformer.Deformer.MeshCalculation(manager.useDelay ? 1 : 0);
                if (PaperStates.Alive(flat)) filter!.sharedMesh = flat;
            }
            catch (Exception rollbackError) { Plugin.ReportOnce("live rollback requires restart", rollbackError); }
            AccessibilitySettings.Reject(true, error);
        }
    }

    private void RestoreAtBoundary(MagicaPhysicsManager manager)
    {
        if (captured)
        {
            if (PaperStates.Alive(filter))
            {
                var current = filter!.sharedMesh;
                if (current != flat && current != original && current != previousMesh)
                    throw new InvalidOperationException("Another owner replaced the paper mesh; restoration was not allowed to overwrite it.");
                filter.sharedMesh = PaperStates.Alive(previousMesh) ? previousMesh : original;
            }
            if (PaperStates.Alive(renderDeformer)) renderDeformer!.SetUserEnable(renderEnable);
            if (PaperStates.Alive(virtualDeformer)) virtualDeformer!.SetUserEnable(virtualEnable);
            if (PaperStates.Alive(cloth)) cloth!.SetUserEnable(clothEnable);
            if (PaperStates.Alive(cloth) && cloth!.Status.IsActive) cloth.ResetCloth();
            if (PaperStates.Alive(renderDeformer) && PaperStates.Alive(filter))
            {
                renderDeformer!.Deformer.MeshCalculation(manager.useDelay ? 1 : 0);
                if (filter!.sharedMesh == flat || renderDeformer.Status.userEnable != renderEnable ||
                    virtualDeformer!.Status.userEnable != virtualEnable || cloth!.Status.userEnable != clothEnable)
                    throw new InvalidOperationException("Original paper references or component enable values were not restored.");
            }
            captured = false;
        }
        if (PaperStates.Alive(flat)) Object.Destroy(flat);
        flat = null;
        Applied = false;
        boundaryOwner = null; computeOwner = null;
    }

    internal bool ReleaseDestroyedOwner(MagicaPhysicsManager manager)
    {
        try
        {
            // Destroying just the Newspaper behaviour does not destroy its mesh
            // consumers. Restore live consumers at their original boundary first.
            if (captured)
            {
                manager.Compute.CompleteJob();
                bool any = PaperStates.Alive(filter) || PaperStates.Alive(cloth)
                    || PaperStates.Alive(virtualDeformer) || PaperStates.Alive(renderDeformer);
                if (any && (!PaperStates.Alive(filter) || !PaperStates.Alive(cloth)
                    || !PaperStates.Alive(virtualDeformer) || !PaperStates.Alive(renderDeformer)))
                    throw new InvalidOperationException("Partially destroyed paper consumers require retention; not safe to free mesh.");
                if (any) RestoreAtBoundary(manager);
            }
            if (PaperStates.Alive(flat)) Object.Destroy(flat);
            flat = null; captured = false; Applied = false;
            boundaryOwner = null; computeOwner = null;
            return true;
        }
        catch (Exception error) { Failed = true; Plugin.ReportOnce("destroyed paper retained", error); return false; }
    }
}

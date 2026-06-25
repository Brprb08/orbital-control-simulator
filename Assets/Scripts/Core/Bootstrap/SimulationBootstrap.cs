using UnityEngine;

/// <summary>
/// Attach to a single "Bootstrap" GameObject. Assign all systems in the Inspector.
/// At runtime, builds a SimContext and calls Initialize() on each system in dependency order.
/// </summary>
public sealed class SimulationBootstrap : MonoBehaviour
{
    [SerializeField] private BootstrapReferences references = new();

    private SimContext ctx;

    /// <summary>
    /// Creates the shared context and initializes all registered systems.
    /// </summary>
    private void Awake()
    {
        if (!BootstrapValidator.TryValidate(references, out string error))
        {
            Debug.LogError(error);
            enabled = false;
            return;
        }

        ctx = SimContextFactory.Create(references);
        BootstrapSequence.Initialize(ctx, references, gameObject);
    }
}

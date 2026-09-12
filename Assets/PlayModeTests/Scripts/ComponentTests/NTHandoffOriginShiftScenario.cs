// Same handoff smoothness invariants as NTHandoffSmoothnessScenario, but with positions
// routed through an INetworkTransformPositionTransform origin shift (absolute double3
// frame) with a distinct origin per process, as used for large-world origin shifting.
public class NTHandoffOriginShiftScenario : NTHandoffSmoothnessScenario
{
    protected override string prefabName => "NTHandoffMoverOrigin";
    protected override int barrierBase => 8810;
}

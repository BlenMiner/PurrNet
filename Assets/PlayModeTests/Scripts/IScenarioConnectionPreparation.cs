using Cysharp.Threading.Tasks;

// Optional setup for targeted standalone scenarios that need different scenes before joining.
public interface IScenarioConnectionPreparation
{
    UniTask BeforeConnection(ScenarioContext ctx);
    UniTask AfterServerStarted(ScenarioContext ctx);
}

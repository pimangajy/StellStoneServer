using System.Threading.Tasks;

namespace GameServer.Effects.Actions
{
    public enum DynamicConditionType
    {
        MyMinionCount,        // 내 필드의 하수인 수
        EnemyMinionCount,     // 상대 필드의 하수인 수
        AllMinionCount,       // 전장의 모든 하수인 수
        MyHandCount,          // 내 손패에 있는 카드 수
        DamagedMinionCount,   // 내 필드의 부상당한 하수인 수
        TribeMinionCount      // 내 필드/멤버존에 있는 특정 종족의 수
    }

    /// <summary>
    /// 손패에 머무는 동안 전장/손패/묘지의 상태에 따라 코스트 및 스탯을 실시간으로 변동시키는 액션입니다.
    /// </summary>
    public class DynamicStatAction : IAction
    {
        public DynamicConditionType ConditionType { get; set; } = DynamicConditionType.MyHandCount;
        public CardTribe? RequiredTribe { get; set; }

        public int CostPerUnit { get; set; } = 0;
        public int AttackPerUnit { get; set; } = 0;
        public int HealthPerUnit { get; set; } = 0;

        public Task ExecuteAsync(GameState state, EffectContext context)
        {
            return Task.CompletedTask;
        }
    }
}

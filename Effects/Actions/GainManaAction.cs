using System.Threading.Tasks;

namespace GameServer.Effects.Actions
{
    /// <summary>
    /// ?€ê²?ì§€???†ì´, ??ì¹´ë“œë¥??¬ìš©???Œë ˆ?´ì–´?ê²Œ ì¦‰ì‹œ ë§ˆë‚˜ë¥?ë¶€?¬í•˜???¡ì…˜?…ë‹ˆ??
    /// </summary>
    public class GainManaAction : IAction
    {
        public int Amount { get; set; }

        public GainManaAction() { }

        public Task ExecuteAsync(GameState state, EffectContext context)
        {
            // ?€ê²?TargetEntity) ê²€?¬ë? ?„ì˜ˆ ?˜ì? ?ŠìŠµ?ˆë‹¤!
            
            // 1. ì¹´ë“œë¥???ì£¼ì¸???íƒœë¥?ê°€?¸ì˜µ?ˆë‹¤.
            PlayerState p = state.GetPlayerState(context.OwnerUid);

            // 2. ìµœë? ë§ˆë‚˜ë¥?ì´ˆê³¼?˜ì—¬ ?„ì‹œ ë§ˆë‚˜ë¥??»ì„ ???ˆë„ë¡?ì§ì ‘ ?”í•´ì¤ë‹ˆ??
            p.CurrentMana += Amount;

            // ì°¸ê³ : ë§ˆë‚˜ ?˜ì¹˜ê°€ ë°”ë€Œë©´ ?¨ê³¼ ì²˜ë¦¬ê°€ ëª¨ë‘ ?ë‚œ ì§í›„ 
            // GameState.BroadcastUpdatesAsync?ì„œ ?Œì•„???´ë¼?´ì–¸??S_UpdateMana)ë¡??™ê¸°?”í•´ ì¤ë‹ˆ??
            state.RaiseEffectLog(context.OwnerUid, context.SourceCard?.CardId, "GainManaAction");
            return Task.CompletedTask;
        }
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using GameServer.Effects.Conditions;

namespace GameServer.Effects.Actions
{
    /// <summary>
    /// ì¡°ê±´(Condition)??ì°?ê±°ì§“ ?¬ë????°ë¼ ?œë¡œ ?¤ë¥¸ ?¡ì…˜??ë¶„ê¸°?˜ì—¬ ?¤í–‰?˜ëŠ” ë§ŒëŠ¥ If-Else ?¡ì…˜?…ë‹ˆ??
    /// </summary>
    public class ConditionalAction : IAction
    {
        // ?‰ê???ì¡°ê±´ (ICondition)
        public ICondition? Condition { get; set; }

        // ì¡°ê±´??ì°?True)?????¤í–‰???¡ì…˜ (?¨ì¼ ?ëŠ” ?¤ì¤‘ ëª©ë¡ ì§€??
        public IAction? ThenAction { get; set; }
        public List<IAction> ThenActions { get; set; } = new List<IAction>();

        // ì¡°ê±´??ê±°ì§“(False)?????¤í–‰???¡ì…˜ (?¨ì¼ ?ëŠ” ?¤ì¤‘ ëª©ë¡ ì§€?? ?ëµ ê°€??
        public IAction? ElseAction { get; set; }
        public List<IAction> ElseActions { get; set; } = new List<IAction>();

        public async Task ExecuteAsync(GameState state, EffectContext context)
        {
            bool isMet = false;
            if (Condition != null)
            {
                isMet = Condition.Check(state, context);
            }

            if (isMet)
            {
                if (ThenAction != null)
                {
                    await ThenAction.ExecuteAsync(state, context);
                }
                if (ThenActions != null)
                {
                    foreach (var action in ThenActions)
                    {
                        await action.ExecuteAsync(state, context);
                    }
                }
            }
            else
            {
                if (ElseAction != null)
                {
                    await ElseAction.ExecuteAsync(state, context);
                }
                if (ElseActions != null)
                {
                    foreach (var action in ElseActions)
                    {
                        await action.ExecuteAsync(state, context);
                    }
                }
            }
        }
    }
}

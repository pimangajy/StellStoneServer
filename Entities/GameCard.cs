using System;
using System.Collections.Generic;
using System.Linq;

namespace GameServer
{
    /// <summary>
    /// [카드 정보 클래스]
    /// 덱이나 손패에 있을 때의 '카드' 그 자체를 나타냅니다.
    /// DB에서 불러온 원본 스탯과, 게임 중 버프/너프된 현재 스탯을 모두 가집니다.
    /// </summary>
    public class GameCard
    {
        public string CardId { get; private set; } // 원본 카드 ID (예: "Fireball")
        public string InstanceId { get; set; }     // 이 게임에서의 고유 ID (예: "Hand_PlayerA_1")
        public string OwnerUid { get; set; } = ""; 
        public string CardName { get; set; }
        
        // --- 원본 스탯 (절대 변하지 않는 기준값) ---
        public int OriginalCost { get; private set; }
        public int OriginalAttack { get; private set; }
        public int OriginalHealth { get; private set; }
        public List<CardKeywords>? OriginalKeywords { get; private set; }

        public CardType? Type;
        public CardClass? Class { get; private set; }  // 직업
        public CardTribe? Tribe { get; private set; }  // 종족 (강도단 등)
        public CardRarity? Rarity { get; private set; } 
        public CardOrigin Origin { get; set; } = CardOrigin.Deck; // 카드의 출처 (덱, 사이드덱, 묘지, 생성)
        public bool TargetRule { get; private set; } // 타겟팅 규칙

        // 새로운 시스템용 효과 리스트와 Zone 상태
        public GameServer.Effects.Zone CurrentZone { get; private set; } = GameServer.Effects.Zone.None;
        public List<GameServer.Effects.CardEffect> NewEffects { get; set; } = new List<GameServer.Effects.CardEffect>();
        public List<MemberSkill> MemberSkills { get; set; } = new List<MemberSkill>();

        // 카드의 위치가 바뀔 때 호출되는 핵심 메서드
        public void UpdateZone(GameServer.Effects.Zone newZone, GameServer.Effects.EventSystem eventSystem)
        {
            if (CurrentZone == newZone) return; // 위치가 그대로면 무시

            // 1. 기존 Zone에서 벗어났으므로, 기존 위치에서 발동하던 반응형(Reactive) 효과들을 구독 해지
            foreach (var effect in NewEffects)
            {
                if (effect.Kind == GameServer.Effects.EffectKind.Reactive && effect.ActiveZone == CurrentZone)
                {
                    eventSystem.Unsubscribe(effect);
                }
            }

            CurrentZone = newZone;

            // 2. 새로운 Zone에 진입했으므로, 새 위치에서 발동해야 할 반응형(Reactive) 효과들을 구독 등록
            foreach (var effect in NewEffects)
            {
                if (effect.Kind == GameServer.Effects.EffectKind.Reactive && effect.ActiveZone == CurrentZone)
                {
                    eventSystem.Subscribe(effect, this);
                }
            }
        }

        // --- 현재 스탯 (게임 중 버프/너프에 의해 변하는 값) ---
        public int CurrentCost { get; set; }
        public int CurrentAttack { get; set; }
        public int CurrentHealth { get; set; }
        public List<CardKeywords> CurrentKeywords { get; set; }
        public List<EnchantmentInfo> Enchantments { get; set; } = new List<EnchantmentInfo>();
        public int CustomValue { get; set; } = 0; // 손패 누적 스택 수치

        // --- 특수 오라 능력 4종 ---
        public int SpellAmp { get; set; } = 0;
        public int SpellWeakness { get; set; } = 0;
        public bool DrawSeal { get; set; } = false;
        public int BuffAmpAttack { get; set; } = 0;
        public int BuffAmpHealth { get; set; } = 0;

        // --- 🚀 [실시간 증폭 정보 (주문 증폭, 버프 증폭)] ---
        public bool IsAmplified { get; set; } = false;        // 현재 증폭 효과 적용 여부 (클라이언트 오라 이펙트용)
        public bool IsSpellAmplified { get; set; } = false;   // 주문 피해 증폭 여부
        public bool IsBuffAmplified { get; set; } = false;    // 버프량 증폭 여부
        public int DynamicDamage { get; set; } = 0;           // 증폭이 반영된 실시간 주문 피해량
        public int BaseDamage { get; set; } = 0;              // 증폭 전 기본 주문 피해량
        public int SpellAmpBonus { get; set; } = 0;           // 적용된 순수 주문 증폭치 (+1, +2 등)
        public int DynamicBuffAttack { get; set; } = 0;       // 증폭이 반영된 실시간 버프 공격력
        public int DynamicBuffHealth { get; set; } = 0;       // 증폭이 반영된 실시간 버프 체력
        public int BaseBuffAttack { get; set; } = 0;          // 증폭 전 기본 버프 공격력
        public int BaseBuffHealth { get; set; } = 0;          // 증폭 전 기본 버프 체력
        public int BuffAmpAtkBonus { get; set; } = 0;         // 적용된 버프 공격력 증폭치
        public int BuffAmpHpBonus { get; set; } = 0;          // 적용된 버프 체력 증폭치

        // [생성자 1] 일반 카드 생성 (DB에서 데이터 로드)
        public GameCard(string cardId, string instanceId)
        {
            CardId = cardId;
            InstanceId = instanceId;
            
            // 싱글톤 DB 매니저에게서 데이터 가져오기
            ServerCardData? data = ServerCardDatabase.Instance.GetCardData(cardId);

            if (data != null)
            {
                // DB 데이터가 있으면 원본 스탯 설정
                CardName = data.Name ?? "이름 없음";
                OriginalCost = data.Cost;
                OriginalAttack = data.AttackValue;
                OriginalHealth = data.HealthValue;
                OriginalKeywords = data.Keywords != null ? new List<CardKeywords>(data.Keywords) : new List<CardKeywords>();
                
                Type = data.CardType ?? CardType.UNKNOWN;
                Class = data.Class ?? CardClass.Gangzi;
                Tribe = data.Tribe ?? CardTribe.강도단;
                Rarity = data.Rarity ?? CardRarity.common; 
                TargetRule = data.Targeting ?? false;
                NewEffects = data.GetNewParsedEffects();
                MemberSkills = data.GetMemberSkills();

                // 오라 특수 능력치 로드
                SpellAmp = data.SpellAmp;
                SpellWeakness = data.SpellWeakness;
                DrawSeal = data.DrawSeal;
                BuffAmpAttack = data.BuffAmpAttack;
                BuffAmpHealth = data.BuffAmpHealth;
            }
            else
            {
                // DB에 없는 카드일 경우 (에러 방지용 기본값)
                Console.WriteLine($"[GameCard] ⚠️ DB에서 카드 데이터를 찾을 수 없습니다: {cardId}");
                CardName = "알 수 없는 카드";
                OriginalCost = 1; OriginalAttack = 1; OriginalHealth = 1;
                Type = CardType.하수인; Class = CardClass.Gangzi; Tribe = CardTribe.무소속; TargetRule = false;
                OriginalKeywords = new List<CardKeywords> { CardKeywords.Default };
                NewEffects = new List<GameServer.Effects.CardEffect>();
                MemberSkills = new List<MemberSkill>();
            }

            // 초기에는 현재 스탯 = 원본 스탯 (멤버는 공격력 없이 0)
            if (Type == CardType.멤버)
            {
                OriginalAttack = 0;
            }
            CurrentCost = OriginalCost;
            CurrentAttack = OriginalAttack;
            CurrentHealth = OriginalHealth;
            CurrentKeywords = new List<CardKeywords>(OriginalKeywords);

            // 주문 카드인 경우 기본 데미지 및 버프량 파싱
            if (Type == CardType.주문 && NewEffects != null)
            {
                var dmgAction = NewEffects.SelectMany(e => e.Actions).OfType<GameServer.Effects.Actions.DamageAction>().FirstOrDefault();
                if (dmgAction != null)
                {
                    BaseDamage = dmgAction.Amount;
                    DynamicDamage = BaseDamage;
                }

                var buffAction = NewEffects.SelectMany(e => e.Actions).OfType<GameServer.Effects.Actions.BuffAction>().FirstOrDefault();
                if (buffAction != null)
                {
                    BaseBuffAttack = buffAction.AttackBuff;
                    BaseBuffHealth = buffAction.HealthBuff;
                    DynamicBuffAttack = BaseBuffAttack;
                    DynamicBuffHealth = BaseBuffHealth;
                }
            }
        }

        // [생성자 2] 영웅(Leader) 카드 생성 (코드에서 직접 생성)
        public GameCard(string instanceId, CardClass playerClass, CardTribe tribe, int health = 30)
        {
            CardId = $"LEADER_{playerClass}";
            InstanceId = instanceId;
            CardName = $"{playerClass} 영웅";

            OriginalCost = 0;
            OriginalAttack = 0;
            OriginalHealth = health;
            OriginalKeywords = new List<CardKeywords> { CardKeywords.Default };

            Type = CardType.READER;
            Class = playerClass;
            Tribe = tribe;
            Rarity = CardRarity.legendary; 
            TargetRule = false;

            CurrentCost = OriginalCost;
            CurrentAttack = OriginalAttack;
            CurrentHealth = OriginalHealth;
            CurrentKeywords = new List<CardKeywords>(OriginalKeywords);
        }

        // 클라이언트에게 보낼 데이터(CardInfo)로 변환
        public CardInfo ToCardInfo()
        {
            return new CardInfo
            {
                cardId = this.CardId,
                instanceId = this.InstanceId,
                cardName = this.CardName,
                origin = this.Origin,
                currentCost = this.CurrentCost,
                currentAttack = this.CurrentAttack,
                currentHealth = this.CurrentHealth,
                customValue = this.CustomValue,
                enchantments = new List<EnchantmentInfo>(this.Enchantments),

                // 🚀 [신규 추가] 실시간 증폭 정보 동기화
                isAmplified = this.IsAmplified,
                isSpellAmplified = this.IsSpellAmplified,
                isBuffAmplified = this.IsBuffAmplified,

                dynamicDamage = this.DynamicDamage,
                baseDamage = this.BaseDamage,
                spellAmpBonus = this.SpellAmpBonus,

                dynamicBuffAttack = this.DynamicBuffAttack,
                dynamicBuffHealth = this.DynamicBuffHealth,
                baseBuffAttack = this.BaseBuffAttack,
                baseBuffHealth = this.BaseBuffHealth,
                buffAmpAtkBonus = this.BuffAmpAtkBonus,
                buffAmpHpBonus = this.BuffAmpHpBonus
            };
        }
    }
}

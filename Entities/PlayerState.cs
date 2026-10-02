using System;
using System.Collections.Generic;

namespace GameServer
{
    /// <summary>
    /// 플레이어 한 명의 게임 내 상태(덱, 손패, 필드, 자원 등)를 관리하는 클래스입니다.
    /// </summary>
    public class PlayerState
    {
        public string Uid { get; private set; }              // 플레이어 고유 식별자
        public GamePlayer PlayerRef { get; private set; }   // 네트워크 통신을 위한 플레이어 객체 참조
        public GameEntity Leader { get; private set; }      // 플레이어의 영웅(본체) 개체
        
        public List<GameCard> Deck { get; private set; }    // 현재 덱에 남은 카드 목록
        public List<GameCard> Hand { get; private set; }    // 현재 손에 들고 있는 카드 목록
        public List<GameCard> SideDeck { get; private set; } = new List<GameCard>(); // 현재 사이드덱에 남은 카드 목록
        public List<GameCard> Graveyard { get; private set; } = new List<GameCard>();
        public List<GameCard> CardsPlayedThisTurn { get; private set; } = new List<GameCard>(); // 이번 턴에 사용한 카드 목록

        public bool HasUsedSideDeckThisTurn { get; set; } = false; // 이번 턴에 사이드덱에서 카드를 가져왔는지 여부 (턴당 1회 제한)

        // 필드 슬롯: [0]~[4]까지 총 5칸의 하수인 배치 구역
        public GameEntity?[] Field { get; private set; } = new GameEntity?[5];
        
        // 멤버 존: 특정 '멤버' 타입의 개체가 배치되는 전용 구역 (1칸)
        public GameEntity?[] MemberZone { get; private set; } = new GameEntity?[1];
        
        public int CurrentMana { get; set; }                // 이번 턴에 사용 가능한 현재 마나
        public int MaxMana = 0;                             // 이번 턴의 최대 마나 한도
        
        private int _nextInstanceId = 1;                    // 카드 인스턴스 ID 발급을 위한 카운터

        // PlayerState 전용 이벤트
        public event Action<string, string>? OnCardDrawn;

        public PlayerState(GamePlayer player, GameEntity leader)
        {
            Uid = player.Uid;
            PlayerRef = player;
            Leader = leader;
            Deck = new List<GameCard>();
            Hand = new List<GameCard>();
            SideDeck = new List<GameCard>();
            Graveyard = new List<GameCard>();

            // 1. 메인 덱 카드를 생성하여 덱에 채움
            if (player.Deck != null && player.Deck.cardIds != null)
            {
                foreach (string cardId in player.Deck.cardIds)
                {
                    string instanceId = $"DeckCard_{Uid}_{_nextInstanceId++}";
                    var card = new GameCard(cardId, instanceId) 
                    { 
                        OwnerUid = Uid,
                        Origin = CardOrigin.Deck
                    };
                    Deck.Add(card);
                }
            }

            // 2. 사이드 덱 카드를 생성하여 SideDeck에 채움
            if (player.Deck != null && player.Deck.sideDeckCardIds != null)
            {
                foreach (string cardId in player.Deck.sideDeckCardIds)
                {
                    string instanceId = $"SideCard_{Uid}_{_nextInstanceId++}";
                    var sideCard = new GameCard(cardId, instanceId) 
                    { 
                        OwnerUid = Uid,
                        Origin = CardOrigin.SideDeck
                    };
                    SideDeck.Add(sideCard);
                }
            }
        }

        /// <summary>
        /// 덱에 있는 카드들의 순서를 무작위로 섞습니다.
        /// </summary>
        public void ShuffleDeck(Random rng)
        {
            int n = Deck.Count;
            while (n > 1) 
            { 
                n--; 
                int k = rng.Next(n + 1); 
                (Deck[k], Deck[n]) = (Deck[n], Deck[k]); 
            }
        }

        public const int MaxHandCount = 10;

        /// <summary>
        /// 덱에서 카드 한 장을 뽑습니다. 손패가 10장 이상이면 소각(Graveyard 직행, isBurned = true)됩니다.
        /// </summary>
        /// <param name="isBurned">손패 초과로 소각되었는지 여부</param>
        /// <returns>뽑은 카드 객체, 덱이 비어있으면 null</returns>
        public GameCard? DrawCard(out bool isBurned)
        {
            isBurned = false;
            if (Deck.Count == 0) return null; // 탈진 상태 등 처리 가능 구역
            
            // 덱의 가장 마지막(위) 카드를 가져옴
            GameCard card = Deck[Deck.Count - 1];
            Deck.RemoveAt(Deck.Count - 1);
            
            // 손/필드/묘지로 들어올 때 클라이언트와 통신할 고유 인스턴스 ID 새로 부여
            card.InstanceId = $"HandCard_{Uid}_{_nextInstanceId++}";

            // 손패 10장 이상이면 소각
            if (Hand.Count >= MaxHandCount)
            {
                Graveyard.Add(card);
                isBurned = true;
                return card;
            }

            Hand.Add(card);
            // 2. 나(PlayerState) 카드 뽑았다고 소리침!
            OnCardDrawn?.Invoke(this.Uid, card.CardId);
            return card;
        }

        /// <summary>
        /// 덱에서 카드 한 장을 뽑습니다. (기존 호환성 유지용 오버로드)
        /// </summary>
        public GameCard? DrawCard()
        {
            return DrawCard(out _);
        }
    }
}

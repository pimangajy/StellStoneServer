using System;
using System.Collections.Generic;
using Google.Cloud.Firestore;

namespace GameServer.Models
{
    /// <summary>
    /// Firestore의 'MatchmakingQueue' 컬렉션에 저장되는 대기열 데이터 모델
    /// 클라이언트의 MatchmakingEntry.cs와 100% 호환됩니다.
    /// </summary>
    [FirestoreData]
    public class MatchmakingQueueEntry
    {
        [FirestoreProperty("status")]
        public string Status { get; set; } = "waiting"; // "waiting", "matched"

        [FirestoreProperty("level")]
        public int Level { get; set; } = 1;

        [FirestoreProperty("deckId")]
        public string? DeckId { get; set; }

        [FirestoreProperty("playerName")]
        public string? PlayerName { get; set; }

        [FirestoreProperty("opponentUid")]
        public string? OpponentUid { get; set; }

        [FirestoreProperty("gameId")]
        public string? GameId { get; set; }

        [FirestoreProperty("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 클라이언트가 서버로 보낼 매칭 요청 바디
    /// </summary>
    public class MatchRequestDto
    {
        public string? DeckId { get; set; }
    }

    /// <summary>
    /// 서버가 클라이언트에 응답할 매칭 상태 DTO
    /// </summary>
    public class MatchResponseDto
    {
        public string Status { get; set; } = "waiting"; // "waiting", "matched", "error"
        public string? GameId { get; set; }
        public string? OpponentUid { get; set; }
        public string? OpponentName { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// 덱 구성 검증 실패 시 클라이언트로 전송되는 전용 에러 패킷 DTO
    /// </summary>
    public class MatchDeckErrorResponse : MatchResponseDto
    {
        public string Action { get; set; } = "MATCH_DECK_ERROR";
        public string? ErrorCode { get; set; } // DECK_NOT_FOUND, DECK_COUNT_MISMATCH, DECK_DUPLICATE_CARD, DECK_CLASS_MISMATCH, DECK_SIDE_DECK_INVALID, DECK_INVALID_CARD
        public int CurrentCount { get; set; }
        public int RequiredCount { get; set; } = 30;
        public List<string> InvalidCardIds { get; set; } = new List<string>();

        public MatchDeckErrorResponse()
        {
            Status = "error";
        }
    }
}


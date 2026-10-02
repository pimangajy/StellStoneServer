using System.Collections.Generic;
using Google.Cloud.Firestore;

namespace GameServer
{
    // Firestore에 저장할 사용자 데이터 모델
    [FirestoreData]
    public class UserData
    {
        [FirestoreProperty]
        public string? Username { get; set; }

        [FirestoreProperty]
        public int Level { get; set; } = 1;

        [FirestoreProperty]
        public int Exp { get; set; } = 0;

        [FirestoreProperty]
        public int Score { get; set; } = 1000;

        [FirestoreProperty]
        public int WinCount { get; set; } = 0;

        [FirestoreProperty]
        public int LossCount { get; set; } = 0;

        [FirestoreProperty]
        public Timestamp CreateTime { get; set; }

        [FirestoreProperty]
        public string? SelectDeck { get; set; }

        [FirestoreProperty]
        public int Gold { get; set; }

        [FirestoreProperty]
        public int Stardust { get; set; }

        [FirestoreProperty]
        public int Stellastone { get; set; }

        [FirestoreProperty]
        public List<string> OwnedSkins { get; set; } = new List<string>();

        [FirestoreProperty]
        public List<string> OwnedEmotes { get; set; } = new List<string>();

        [FirestoreProperty]
        public Dictionary<string, int> OwnedCards { get; set; } = new Dictionary<string, int>();

        [FirestoreProperty]
        public Dictionary<string, int> OwnedPrismCards { get; set; } = new Dictionary<string, int>();

        [FirestoreProperty]
        public Dictionary<string, int> OwnedPacks { get; set; } = new Dictionary<string, int>();

        [FirestoreProperty]
        public Dictionary<string, int> PurchaseCounts { get; set; } = new Dictionary<string, int>();
    }
}

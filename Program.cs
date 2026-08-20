using Google.Cloud.Firestore;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;


namespace GameServer
{
    // --- 요청/응답에 사용할 데이터 모델 정의 ---
    // Unity의 SinginManager.cs에 있는 클래스와 유사하게 정의합니다.
    public class SignupRequest
    {
        public string? email { get; set; }
        public string? password { get; set; }
        public string? username { get; set; }
    }

    public class LoginRequest
    {
        public string? email { get; set; }
        public string? password { get; set; }
    }

    public enum ShopCategory
    {
        LeaderSkin = 0, // 리더 스킨
        Emote = 1,      // 이모티콘
        CardBack = 2,   // 카드 뒷면
        MapDecor = 3,   // 맵 꾸미기
        PrismCard = 4,  // 프리즘 카드
        Profile = 5,    // 프로필
        SeasonPass = 6, // 시즌 패스
        Package = 7,    // 패키지
        CardPack = 8,   // 카드 팩
        Cards = 9,      // 카드 낱장
    }

    public enum PriceCurrency
    {
        Gold = 0,        // 골드 (일반 인게임 재화)
        Stellastone = 1, // 성석 (유료 결제 재화)
        Stardust = 2     // 별가루 (카드 제작/분해 재화)
    }

    [FirestoreData]
    public class ProductData
    {
        [FirestoreProperty]
        public ShopCategory category_Id { get; set; }
        [FirestoreProperty]
        public string productId { get; set; } = "";
        [FirestoreProperty]
        public string productName { get; set; } = "";
        [FirestoreProperty]
        public string description { get; set; } = "";
        [FirestoreProperty]
        public string image_url { get; set; } = "";
        [FirestoreProperty]
        public int price { get; set; }
        [FirestoreProperty]
        public PriceCurrency currency { get; set; }
        [FirestoreProperty]
        public bool isActive { get; set; } = true;
        [FirestoreProperty]
        public string sale_Start_Date { get; set; } = "2026-01-01 00:00:00";
        [FirestoreProperty]
        public string sale_End_Date { get; set; } = "2099-12-31 23:59:59";
    }

    public enum CurrencyType
    {
        Gold = 0,        // 골드
        Stellastone = 1, // 성석 (유료 재화)
        Stardust = 2     // 별가루
    }

    public class PurchaseRequest
    {
        public string productId { get; set; } = "";
        public int quantity { get; set; } = 1;
    }

    public class PurchaseResponse
    {
        public string status { get; set; } = "success";
        public string message { get; set; } = "";
        public string productId { get; set; } = "";
        public int quantity { get; set; }
        public int remainingGold { get; set; }
        public int remainingStellastone { get; set; }
        public int remainingStardust { get; set; }
        public List<string> obtainedCardIds { get; set; } = new List<string>();
        public string? obtainedItemId { get; set; }
    }

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
    }

    [FirestoreData]
    public class DeckData
    {
        // deckId는 Firestore 문서의 ID이므로, Firestore 필드 속성을 붙이지 않습니다.
        // (클라이언트로 보낼 때만 이 속성에 ID를 채워줍니다.)
        public string? deckId { get; set; }

        [FirestoreProperty]
        public string? deckName { get; set; }
        [FirestoreProperty]
        public string? deckClass { get; set; }
        [FirestoreProperty]
        public List<string>? cardIds { get; set; }

        [FirestoreProperty]
        public List<string>? sideDeckCardIds { get; set; }
    
        [FirestoreProperty]
        public List<string>? sideDeckFirstTurnCardIds { get; set; }

        // Firestore 변환을 위한 기본 생성자
        public DeckData()
        {
            sideDeckCardIds = new List<string>();
            sideDeckFirstTurnCardIds = new List<string>();
        }

        // 서버 코드 내에서 객체 생성을 위한 생성자
        public DeckData(string name, string className)
        {
            deckName = name;
            deckClass = className;
            cardIds = new List<string>(); // 새 덱은 항상 비어있는 카드 리스트로 시작
            sideDeckCardIds = new List<string>();
            sideDeckFirstTurnCardIds = new List<string>();
        }
    }

    // 덱 생성 시 클라이언트(Unity)가 보낼 데이터
    public class CreateDeckRequest
    {
        public string? className { get; set; }
    }
    public class SelectDeckRequest
    {
        public string? DeckId { get; set; }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            // 콘솔 출력 시 한글 깨짐 방지를 위해 인코딩을 UTF-8로 설정
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var builder = WebApplication.CreateBuilder(args);

            // --- appsettings.json에서 Firebase 설정 읽기 ---
            var firebaseConfig = builder.Configuration.GetSection("Firebase");
            string? projectId = firebaseConfig["ProjectId"];
            string? credentialPath = firebaseConfig["CredentialPath"];

            if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(credentialPath))
            {
                Console.WriteLine("❌ Firebase 설정(ProjectId, CredentialPath)이 appsettings.json에 없습니다.");
                return; // 설정이 없으면 애플리케이션을 시작하지 않습니다.
            }

            // --- Google Credential 객체 생성 (파일을 한 번만 읽도록 개선) ---
            var credential = GoogleCredential.FromFile(credentialPath);

            // --- Firebase Admin SDK 초기화 (Authentication, Firestore 등) ---
            FirebaseApp.Create(new AppOptions()
            {
                Credential = credential,
            });

            // --- 의존성 주입(Dependency Injection) 설정 ---
            // FirestoreDb 인스턴스를 싱글턴(Singleton)으로 등록하여 애플리케이션 전체에서 공유합니다.
            FirestoreDb firestoreDb = new FirestoreDbBuilder
            {
                ProjectId = projectId,
                Credential = credential
            }.Build();

            builder.Services.AddSingleton(firestoreDb);
            Console.WriteLine("✅ Firebase Admin SDK가 성공적으로 초기화되었습니다.");

            // 대시보드 전용
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAdminDashboard", policy =>
                {
                    // 테스트를 위해 모든 출처(로컬 파일 포함)의 요청을 허용합니다.
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                });
            });

            // ==================================================================
            // (신규) 서버 시작 전 카드 및 상점 데이터베이스 로드
            // ==================================================================
            await ServerCardDatabase.Instance.InitializeAsync(firestoreDb);
            await ServerProductDatabase.Instance.InitializeAsync(firestoreDb);

            var app = builder.Build();

            // 대시보드 전용
            app.UseCors("AllowAdminDashboard");

            // --- HTTP 서버 기본 설정 ---
            // 기본적으로 http://localhost:5000, https://localhost:5001 에서 실행됩니다.

            // --- API 엔드포인트(Endpoint) 정의 ---

            // 1. 회원가입 API: POST /api/auth/signup
            app.MapPost("/api/auth/signup", async (SignupRequest req, FirestoreDb db) =>
            {
                Console.WriteLine($"📧 회원가입 요청 수신: {req.email}");
                if (string.IsNullOrEmpty(req.email) || string.IsNullOrEmpty(req.password))
                {
                    return Results.BadRequest(new { status = "error", message = "이메일과 비밀번호는 필수입니다." });
                }
                // 비밀번호 길이 유효성 검사 추가
                if (req.password.Length < 6)
                {
                    return Results.BadRequest(new { status = "error", message = "비밀번호는 6자리 이상이어야 합니다." });
                }

                try
                {
                    // 1. Firebase Authentication에 사용자 생성
                    UserRecordArgs args = new UserRecordArgs()
                    {
                        Email = req.email,
                        Password = req.password,
                        DisplayName = req.username,
                        Disabled = false,
                    };
                    UserRecord userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(args);
                    Console.WriteLine($"✅ Firebase Auth에 사용자 생성 성공: {userRecord.Uid} ({userRecord.Email})");

                    // 2. Firestore에 추가 사용자 정보 저장 (UID를 문서 ID로 사용)
                    DocumentReference docRef = db.Collection("Users").Document(userRecord.Uid);
                    UserData userData = new UserData
                    {
                        Username = req.username,
                        Level = 1,
                        CreateTime = Timestamp.GetCurrentTimestamp(),
                        Gold = 0,
                        Stardust = 0,
                        Stellastone = 0,
                        OwnedSkins = new List<string>(),
                        OwnedEmotes = new List<string>(),
                        OwnedCards = new Dictionary<string, int>()
                    };
                    await docRef.SetAsync(userData);
                    Console.WriteLine($"✅ Firestore에 사용자 정보 저장 성공: {userRecord.Uid}");

                    // 'Decks' 서브컬렉션에 기본 덱을 생성합니다.
                    CollectionReference decksRef = docRef.Collection("Decks");

                    // 기본 덱 데이터 생성
                    string initialDeckId = "testDeck_1";
                    DeckData initialDeck = new DeckData
                    {
                        deckId = initialDeckId,
                        deckName = "테스트 덱",
                        deckClass = "임시 직업",
                        cardIds = new List<string>() // 비어있는 string 배열
                    };

                    // 'testDeck_1'이라는 ID로 문서를 생성하고 데이터를 저장합니다.
                    await decksRef.Document(initialDeckId).SetAsync(initialDeck);
                    Console.WriteLine($"✅ 'Decks' 서브컬렉션에 기본 덱 생성 완료 (ID: {initialDeckId}): {userRecord.Uid}");

                    // 3. 클라이언트에 성공 응답 전송
                    return Results.Ok(new { status = "success", message = "회원가입 성공!", user_id = userRecord.Uid });
                }
                catch (FirebaseAuthException ex)
                {
                    // 이메일 중복 등 Firebase Auth 관련 오류 처리
                    Console.WriteLine($"❌ 회원가입 실패: {ex.Message}");
                    return Results.Conflict(new { status = "error", message = ex.Message });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });

            // ==================================================================
            // 2. 덱 목록 불러오기 API: GET /api/decks 
            // ==================================================================
            app.MapGet("/api/decks", async (
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                Console.WriteLine("✅ 덱 목록 요청 수신");
                // 1. 토큰 검증
                string? uid = await VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                try
                {
                    // 2. 유저의 Decks 서브컬렉션 참조
                    CollectionReference decksRef = db.Collection("Users").Document(uid).Collection("Decks");
                    QuerySnapshot snapshot = await decksRef.GetSnapshotAsync();

                    List<DeckData> allDecks = new List<DeckData>();
                    foreach (var doc in snapshot.Documents)
                    {
                        DeckData deck = doc.ConvertTo<DeckData>();
                        deck.deckId = doc.Id; // (중요) 문서 ID를 deckId 필드에 수동으로 할당
                        allDecks.Add(deck);
                    }

                    Console.WriteLine($"✅ 덱 목록 {allDecks.Count}개 반환 - 유저: {uid}");

                    // 3. Unity의 JsonUtility가 리스트를 잘 파싱할 수 있도록 래퍼(wrapper) 객체로 감싸서 반환
                    return Results.Ok(new { decks = allDecks });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 덱 목록 불러오기 중 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });

            // ==================================================================
            // 3. 덱 생성 API: POST /api/decks/create 
            // ==================================================================
            app.MapPost("/api/decks/create", async (
                CreateDeckRequest req,
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                // 1. 토큰 검증
                string? uid = await VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                if (req.className == null)
                {
                    return Results.BadRequest(new { status = "error", message = "직업(className) 정보가 없습니다." });
                }

                try
                {
                    // 2. 유저의 Decks 서브컬렉션 참조
                    CollectionReference decksRef = db.Collection("Users").Document(uid).Collection("Decks");

                    // 3. "새로운 덱 X" 로직 (Unity의 DeckSaveManager_Firebase.cs와 동일)
                    const string defaultDeckNamePrefix = "새로운 덱 ";
                    QuerySnapshot snapshot = await decksRef.GetSnapshotAsync();

                    var existingNumbers = snapshot.Documents
                        .Select(doc => doc.ConvertTo<DeckData>().deckName)
                        .Where(name => name != null && name.StartsWith(defaultDeckNamePrefix))
                        .Select(name =>
                        {
                            string numberPart = name!.Substring(defaultDeckNamePrefix.Length);
                            int.TryParse(numberPart, out int number);
                            return number;
                        })
                        .Where(number => number > 0)
                        .ToHashSet();

                    int newDeckNumber = 1;
                    while (existingNumbers.Contains(newDeckNumber))
                    {
                        newDeckNumber++;
                    }

                    string deckName = $"{defaultDeckNamePrefix}{newDeckNumber}";

                    // 4. 새 덱 데이터 생성
                    DeckData newDeck = new DeckData(deckName, req.className);

                    // 5. Firestore에 문서 추가 (Firestore가 ID 자동 생성)
                    DocumentReference addedDocRef = await decksRef.AddAsync(newDeck);
                    Console.WriteLine($"✅ 덱 생성 성공 (ID: {addedDocRef.Id}) - 유저: {uid}");

                    // 6. 클라이언트에 반환할 데이터에 자동 생성된 ID 포함
                    newDeck.deckId = addedDocRef.Id;

                    return Results.Ok(newDeck); // 생성된 덱 정보를 클라이언트에 반환
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 덱 생성 중 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });

            // 2. 로그인(토큰 검증) API: POST /api/auth/verify-token
            // 클라이언트(Unity)가 Firebase SDK로 로그인 후 받은 ID 토큰을 이 API로 보내 검증합니다.
            app.MapPost("/api/auth/verify-token", async (
                IDictionary<string, string> req,
                FirestoreDb db) =>
            {
                string idToken = req["token"];
                FirebaseToken decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
                string uid = decodedToken.Uid;
                Console.WriteLine($"✅ 토큰 검증 성공, 로그인 유저: {uid}");

                // 기존 유저 로그인 시 누락된 항목(Gold, Level, CreateTime, 기본 덱 등) 자동 생성/보정
                await EnsureUserDataIntegrityAsync(uid, db);

                return Results.Ok(new { status = "success", message = "로그인 성공!", user_id = uid });
            });

            // ==================================================================
            // 4. 덱 업데이트 API: PUT /api/decks/update/{deckId} 
            // ==================================================================
            app.MapPut("/api/decks/update/{deckId}", async (
                string deckId,  // 1. URL 경로에서 수정할 덱의 ID를 받습니다.
                DeckData updatedDeck, // 2. 요청 본문(Body)에서 수정된 덱 데이터를 받습니다.
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                // 3. 토큰 검증
                string? uid = await VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                // 4. (중요) 서버 측 검증 (Anti-Cheat)
                // 4-1. 덱 이름이 비어있는지 검사
                if (string.IsNullOrWhiteSpace(updatedDeck.deckName))
                {
                    return Results.BadRequest(new { status = "error", message = "덱 이름은 비워둘 수 없습니다." });
                }
                // 4-2. 덱 카드 수가 30장을 초과하는지 검사 (핵심 치팅 방지)
                if (updatedDeck.cardIds != null && updatedDeck.cardIds.Count > 30) // 30은 최대 덱 크기
                {
                    Console.WriteLine($"덱 크기 초과 ({updatedDeck.cardIds.Count}장)");
                    return Results.BadRequest(new { status = "error", message = "덱은 30장을 초과할 수 없습니다." });
                }
                // 4-3. 사이드덱 검증 
                if (updatedDeck.sideDeckCardIds != null && updatedDeck.sideDeckCardIds.Count > 5) 
                {
                    return Results.BadRequest(new { status = "error", message = "사이드 덱은 5장을 초과할 수 없습니다." });
                }
                // 4-4. 선공용 사이드덱 검증
                if (updatedDeck.sideDeckFirstTurnCardIds != null && updatedDeck.sideDeckFirstTurnCardIds.Count > 3) 
                {
                    return Results.BadRequest(new { status = "error", message = "선공 사이드 덱은 3장을 초과할 수 없습니다." });
                }
                // TODO: (고급) cardIds의 각 카드가 유효한지, 유저가 소유한 카드인지, 직업/희귀도 규칙을 지켰는지 검증해야 합니다.

                try
                {
                    // 5. Firestore 문서 경로 지정 (중요: {uid}를 경로에 포함하여 본인 덱만 수정하도록 강제)
                    DocumentReference deckRef = db.Collection("Users").Document(uid).Collection("Decks").Document(deckId);

                    // 6. 덱 업데이트 (SetAsync 사용)
                    await deckRef.SetAsync(updatedDeck, SetOptions.Overwrite); // 덮어쓰기

                    Console.WriteLine($"✅ 덱 업데이트 성공 (ID: {deckId}) - 유저: {uid}");
                    return Results.Ok(new { status = "success", message = "덱이 저장되었습니다." });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 덱 업데이트 중 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });

            // ==================================================================
            // 5. 덱 삭제 API: DELETE /api/decks/delete/{deckId} 
            // ==================================================================
            app.MapDelete("/api/decks/delete/{deckId}", async (
                string deckId,  // 1. URL 경로에서 삭제할 덱의 ID를 받습니다.
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                // 2. 토큰 검증
                string? uid = await VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                if (string.IsNullOrEmpty(deckId))
                {
                    return Results.BadRequest(new { status = "error", message = "덱 ID가 필요합니다." });
                }

                try
                {
                    // 3. Firestore 문서 경로 지정 (중요: {uid}를 경로에 포함)
                    DocumentReference deckRef = db.Collection("Users").Document(uid).Collection("Decks").Document(deckId);

                    // 4. 덱 삭제 실행
                    await deckRef.DeleteAsync();

                    Console.WriteLine($"✅ 덱 삭제 성공 (ID: {deckId}) - 유저: {uid}");
                    return Results.Ok(new { status = "success", message = "덱이 삭제되었습니다." });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 덱 삭제 중 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });

            // ==================================================================
            // 6. 대표 덱 선택 API: PUT /api/user/select-deck
            // ==================================================================
            app.MapPut("/api/user/select-deck", async (
                SelectDeckRequest req, // 1. 요청 본문(Body)에서 선택한 덱 ID를 받습니다.
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                // 2. 토큰 검증
                string? uid = await VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                if (string.IsNullOrEmpty(req.DeckId))
                {
                    return Results.BadRequest(new { status = "error", message = "DeckId가 비어있습니다." });
                }

                try
                {
                    // 3. Firestore의 Users/{uid} 문서 참조
                    DocumentReference userDocRef = db.Collection("Users").Document(uid);

                    // 4. "SelectDeck" 필드만 특정 덱 ID로 업데이트
                    await userDocRef.UpdateAsync("SelectDeck", req.DeckId);

                    Console.WriteLine($"대표 덱 선택 성공 (DeckID: {req.DeckId}) - 유저: {uid}");
                    return Results.Ok(new { status = "success", message = "대표 덱이 성공적으로 업데이트되었습니다." });
                }
                catch (Exception ex)
                {
                    // Firestore 문서가 없는 경우 등 예외 처리
                    Console.WriteLine($" 대표 덱 선택 중 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });

            // ==================================================================
            // 6-2. (신규) 대표 덱 조회 API: GET /api/user/select-deck
            // ==================================================================
            app.MapGet("/api/user/select-deck", async (
            FirestoreDb db,
            [FromHeader(Name = "Authorization")] string authorization) =>
            {
            // 1. 토큰 검증
            string? uid = await VerifyTokenAsync(authorization);
            if (uid == null)
            {
                return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
            }

            try
            {
                // 2. Firestore의 Users/{uid} 문서 참조
                DocumentReference userDocRef = db.Collection("Users").Document(uid);
                DocumentSnapshot userSnapshot = await userDocRef.GetSnapshotAsync();

                if (!userSnapshot.Exists)
                {
                    return Results.NotFound(new { status = "error", message = "유저 정보를 찾을 수 없습니다." });
                }

                // 3. UserData로 변환하여 SelectDeck(덱 ID) 추출
                UserData? userData = userSnapshot.ConvertTo<UserData>();
                string? selectedDeckId = userData?.SelectDeck;

                if (string.IsNullOrEmpty(selectedDeckId))
                {
                    // 선택된 덱이 없으면 null 반환
                    return Results.Ok(new { status = "success", deck = (DeckData?)null });
                }

                // 4. (추가) 덱 ID를 사용하여 실제 덱 문서 가져오기
                DocumentReference deckDocRef = userDocRef.Collection("Decks").Document(selectedDeckId);
                DocumentSnapshot deckSnapshot = await deckDocRef.GetSnapshotAsync();

                if (!deckSnapshot.Exists)
                {
                    // 선택된 덱 ID는 있는데 실제 덱 문서가 삭제된 경우
                    return Results.Ok(new { status = "success", deck = (DeckData?)null, message = "선택된 덱을 찾을 수 없습니다." });
                }

                // 5. DeckData 객체로 변환
                DeckData selectedDeck = deckSnapshot.ConvertTo<DeckData>();
                selectedDeck.deckId = deckSnapshot.Id; // ID 수동 할당

                Console.WriteLine($"대표 덱 데이터 조회 성공 (Deck: {selectedDeck.deckName}) - 유저: {uid}");
                
                // 6. 덱 전체 데이터 반환
                return Results.Ok(new { status = "success", deck = selectedDeck });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 대표 덱 조회 중 서버 오류: {ex.Message}");
                return Results.Problem("서버 내부 오류가 발생했습니다.");
            }
        });

            // ==================================================================
            // 상점 상품 목록 조회 API: GET /api/shop/products
            // ==================================================================
            app.MapGet("/api/shop/products", (int? category_id) =>
            {
                var filtered = ServerProductDatabase.Instance.GetProducts(category_id);

                Console.WriteLine($"[Shop] 🛒 상품 목록 조회 요청: category_id={category_id} (반환: {filtered.Count}개)");

                return Results.Ok(new
                {
                    status = "success",
                    message = "상품 목록 조회 성공",
                    data = filtered
                });
            });

            // ==================================================================
            // 상점 상품 구매 API: POST /api/shop/purchase
            // ==================================================================
            app.MapPost("/api/shop/purchase", async (
                PurchaseRequest req,
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                string? uid = await VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                if (req.quantity <= 0) req.quantity = 1;

                try
                {
                    DocumentReference userRef = db.Collection("Users").Document(uid);
                    DocumentSnapshot snapshot = await userRef.GetSnapshotAsync();
                    if (!snapshot.Exists)
                    {
                        return Results.Json(new { status = "error", message = "유저 정보를 찾을 수 없습니다." }, statusCode: 404);
                    }

                    UserData userData = snapshot.ConvertTo<UserData>();

                    ProductData? product = ServerProductDatabase.Instance.GetProduct(req.productId);
                    if (product == null || !product.isActive)
                    {
                        return Results.Json(new { status = "error", message = "존재하지 않거나 판매가 종료된 상품입니다." });
                    }

                    int totalCost = product.price * req.quantity;
                    CurrencyType currencyType = (CurrencyType)(int)product.currency;
                    int packCountToOpen = 0;
                    string? skinToGrant = null;
                    string? emoteToGrant = null;
                    int goldToGrant = 0;

                    if (product.category_Id == ShopCategory.CardPack)
                    {
                        packCountToOpen = 1 * req.quantity;
                    }
                    else if (product.category_Id == ShopCategory.LeaderSkin)
                    {
                        skinToGrant = product.productId;
                        if (userData.OwnedSkins.Contains(skinToGrant))
                        {
                            return Results.Json(new { status = "error", message = "이미 보유 중인 스킨입니다." });
                        }
                    }
                    else if (product.category_Id == ShopCategory.Emote)
                    {
                        emoteToGrant = product.productId;
                        if (userData.OwnedEmotes.Contains(emoteToGrant))
                        {
                            return Results.Json(new { status = "error", message = "이미 보유 중인 이모티콘입니다." });
                        }
                    }

                    // 1. 재화 잔액 검증
                    if (currencyType == CurrencyType.Gold && userData.Gold < totalCost)
                    {
                        return Results.Json(new { status = "error", message = $"골드가 부족합니다. (필요: {totalCost}, 보유: {userData.Gold})" });
                    }
                    if (currencyType == CurrencyType.Stellastone && userData.Stellastone < totalCost)
                    {
                        return Results.Json(new { status = "error", message = $"성석이 부족합니다. (필요: {totalCost}, 보유: {userData.Stellastone})" });
                    }
                    if (currencyType == CurrencyType.Stardust && userData.Stardust < totalCost)
                    {
                        return Results.Json(new { status = "error", message = $"별가루가 부족합니다. (필요: {totalCost}, 보유: {userData.Stardust})" });
                    }

                    // 2. 재화 차감
                    if (currencyType == CurrencyType.Gold) userData.Gold -= totalCost;
                    else if (currencyType == CurrencyType.Stellastone) userData.Stellastone -= totalCost;
                    else if (currencyType == CurrencyType.Stardust) userData.Stardust -= totalCost;

                    // 3. 보상 지급
                    if (goldToGrant > 0)
                    {
                        userData.Gold += goldToGrant;
                    }

                    if (!string.IsNullOrEmpty(skinToGrant) && !userData.OwnedSkins.Contains(skinToGrant))
                    {
                        userData.OwnedSkins.Add(skinToGrant);
                    }

                    if (!string.IsNullOrEmpty(emoteToGrant) && !userData.OwnedEmotes.Contains(emoteToGrant))
                    {
                        userData.OwnedEmotes.Add(emoteToGrant);
                    }

                    List<string> obtainedCards = new List<string>();
                    if (packCountToOpen > 0)
                    {
                        Random rng = new Random();
                        var allCards = ServerCardDatabase.Instance.GetAllCards();
                        var commonCards = allCards.Where(c => c.Rarity == CardRarity.common).ToList();
                        var rareCards = allCards.Where(c => c.Rarity == CardRarity.rare).ToList();
                        var epicCards = allCards.Where(c => c.Rarity == CardRarity.epic).ToList();
                        var legCards = allCards.Where(c => c.Rarity == CardRarity.legendary).ToList();

                        for (int p = 0; p < packCountToOpen; p++)
                        {
                            for (int i = 0; i < 5; i++)
                            {
                                int roll = rng.Next(1, 10001);
                                ServerCardData? chosen = null;

                                // 전설: 2.0%
                                if (roll <= 200 && legCards.Count > 0) chosen = legCards[rng.Next(legCards.Count)];
                                // 특급: 6.5%
                                else if (roll <= 850 && epicCards.Count > 0) chosen = epicCards[rng.Next(epicCards.Count)];
                                // 희귀: 21.5%
                                else if (roll <= 3000 && rareCards.Count > 0) chosen = rareCards[rng.Next(rareCards.Count)];
                                // 일반: 70.0%
                                else if (commonCards.Count > 0) chosen = commonCards[rng.Next(commonCards.Count)];
                                else if (allCards.Count > 0) chosen = allCards[rng.Next(allCards.Count)];

                                if (chosen != null && !string.IsNullOrEmpty(chosen.CardID))
                                {
                                    obtainedCards.Add(chosen.CardID);
                                    if (userData.OwnedCards.ContainsKey(chosen.CardID))
                                    {
                                        userData.OwnedCards[chosen.CardID]++;
                                    }
                                    else
                                    {
                                        userData.OwnedCards[chosen.CardID] = 1;
                                    }
                                }
                            }
                        }
                    }

                    // 4. Firestore 유저 문서 저장
                    await userRef.SetAsync(userData, SetOptions.MergeAll);
                    Console.WriteLine($"✅ [Shop] 상품 구매 완료: 유저 {uid} | 상품ID: {req.productId} | 수량: {req.quantity}");

                    PurchaseResponse res = new PurchaseResponse
                    {
                        status = "success",
                        message = "구매가 완료되었습니다.",
                        productId = req.productId,
                        quantity = req.quantity,
                        remainingGold = userData.Gold,
                        remainingStellastone = userData.Stellastone,
                        remainingStardust = userData.Stardust,
                        obtainedCardIds = obtainedCards,
                        obtainedItemId = skinToGrant ?? emoteToGrant
                    };

                    return Results.Ok(res);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ [Shop] 상품 구매 중 서버 오류: {ex.Message}");
                    return Results.Json(new { status = "error", message = "구매 처리 중 서버 오류가 발생했습니다." }, statusCode: 500);
                }
            });

            // ==================================================================
            // 7. WebSocket 미들웨어 활성화 (신규 추가)
            // ==================================================================
            // HTTP 파이프라인에 WebSocket 기능을 추가합니다.
            // app.Map... 호출 전에 위치해야 합니다.
            app.UseWebSockets();

            // ==================================================================
            // 8. 실시간 대전 (WebSocket) 엔드포인트: GET /ws/game (신규 추가)
            // ==================================================================
            app.MapGet("/ws/game", async (
                HttpContext context, 
                FirestoreDb db 
                ) =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    Console.WriteLine($"WebSocket 연결 요청 수신: {context.Connection.Id}");
                    try
                    {
                        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        Console.WriteLine($"✅ WebSocket 연결 성공: {context.Connection.Id}");

                        // 3. (수정) 
                        // GameSocketHandler로 연결 처리를 위임합니다.
                        // (이제 Program.cs와 GameSocketHandler.cs가 동일한 'GameServer' 
                        // namespace에 속하므로, 컴파일러가 이 클래스를 찾을 수 있습니다.)
                        await GameSocketHandler.HandleConnectionAsync(context, webSocket, db);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ WebSocket 연결 수락/처리 중 최상위 오류: {ex.Message}");
                        if (!context.Response.HasStarted)
                        {
                            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        }
                    }
                }
                else
                {
                    Console.WriteLine("❌ 비-WebSocket 요청이 /ws/game으로 수신됨");
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            });

            // 대시보드 전용
            // 1. 활성화된 방 목록 API
            app.MapGet("/api/admin/rooms", () =>
            {
                // 보안: 실제 환경에서는 여기에 관리자 인증 토큰 검증 로직이 들어가야 합니다.
                var activeRooms = GameRoomManager.GetActiveRoomIds();
                return Results.Ok(new { status = "success", count = activeRooms.Count, rooms = activeRooms });
            });

            // 2. 특정 방의 상세 상태 API
            app.MapGet("/api/admin/rooms/{gameId}", (string gameId) =>
            {
             var room = GameRoomManager.GetRoom(gameId);
             if (room == null) return Results.NotFound(new { status = "error", message = "방을 찾을 수 없습니다." });

             var gameState = room.GetCurrentGameState();
             if (gameState == null) return Results.Ok(new { status = "success", state = "대기 중 (게임 미시작)" });

              var snapshot = gameState.GetSnapshot();
             return Results.Ok(new { status = "success", data = snapshot });
            });

            Console.WriteLine("✅ HTTP 서버가 시작됩니다. Listening on http://*:5123");
            // HTTP 서버 실행
            app.Run();
        }



        /// <summary>
        /// Request Header의 Authorization (Bearer 토큰)을 검증하고 UID를 반환합니다.
        /// 실패 시 null을 반환합니다.
        /// </summary>
        private static async Task<string?> VerifyTokenAsync(string authorization)
        {
            if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer "))
            {
                Console.WriteLine("❌ 토큰이 없거나 'Bearer ' 형식이 아닙니다.");
                return null;
            }

            string idToken = authorization.Substring("Bearer ".Length);

            // 새로 만든 public 헬퍼 함수를 호출합니다.
            return await VerifyTokenStringAsync(idToken);
        }
        
        /// <summary>
        /// 오직 ID 토큰 문자열만 받아 검증하고 UID를 반환하는 'public' 헬퍼 함수입니다.
        /// GameSocketHandler에서 이 함수를 호출합니다.
        /// </summary>
        public static async Task<string?> VerifyTokenStringAsync(string idToken)
        {
            if (string.IsNullOrEmpty(idToken))
            {
                Console.WriteLine("❌ 토큰 문자열이 비어있습니다.");
                return null;
            }
            
            try
            {
                // Firebase Admin SDK를 사용하여 토큰을 검증합니다.
                FirebaseToken decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
                // 검증 성공 시 UID 반환
                return decodedToken.Uid;
            }
            catch (FirebaseAuthException ex)
            {
                Console.WriteLine($"❌ 토큰 문자열 검증 실패 (Firebase): {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 토큰 문자열 검증 중 알 수 없는 오류: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 유저 접속/로그인 시 Firestore Users/{uid} 문서 및 누락된 필드를 대조하여 자동 보정 및 생성합니다.
        /// </summary>
        public static async Task EnsureUserDataIntegrityAsync(string uid, FirestoreDb db)
        {
            if (string.IsNullOrEmpty(uid)) return;

            try
            {
                DocumentReference userDocRef = db.Collection("Users").Document(uid);
                DocumentSnapshot snapshot = await userDocRef.GetSnapshotAsync();

                if (!snapshot.Exists)
                {
                    // 1. 유저 문서가 전혀 없는 경우 Auth 정보 기반으로 신규 생성
                    UserRecord? userRecord = null;
                    try
                    {
                        userRecord = await FirebaseAuth.DefaultInstance.GetUserAsync(uid);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[UserDataSync] Auth 유저 정보 조회 실패: {ex.Message}");
                    }

                    string displayName = !string.IsNullOrEmpty(userRecord?.DisplayName)
                        ? userRecord.DisplayName
                        : (!string.IsNullOrEmpty(userRecord?.Email) ? userRecord.Email.Split('@')[0] : "Player");

                    UserData newUserData = new UserData
                    {
                        Username = displayName,
                        Level = 1,
                        CreateTime = Timestamp.GetCurrentTimestamp(),
                        Gold = 0,
                        Stardust = 0,
                        Stellastone = 0,
                        OwnedSkins = new List<string>(),
                        OwnedEmotes = new List<string>(),
                        OwnedCards = new Dictionary<string, int>()
                    };

                    await userDocRef.SetAsync(newUserData);
                    Console.WriteLine($"✅ [UserDataSync] 기존 유저 {uid}의 UserData 문서 신규 생성 완료 (Gold: 0, Stardust: 0, Stellastone: 0)");

                    // 기본 덱 생성
                    CollectionReference decksRef = userDocRef.Collection("Decks");
                    string initialDeckId = "testDeck_1";
                    DeckData initialDeck = new DeckData
                    {
                        deckId = initialDeckId,
                        deckName = "테스트 덱",
                        deckClass = "임시 직업",
                        cardIds = new List<string>()
                    };
                    await decksRef.Document(initialDeckId).SetAsync(initialDeck);
                    Console.WriteLine($"✅ [UserDataSync] 기본 덱 생성 완료: {uid}");
                }
                else
                {
                    // 2. 유저 문서가 존재할 때 누락된 필드가 있는지 확인하여 부분 갱신
                    Dictionary<string, object> updates = new Dictionary<string, object>();
                    Dictionary<string, object> currentData = snapshot.ToDictionary() ?? new Dictionary<string, object>();

                    if (!currentData.ContainsKey("Gold"))
                    {
                        updates["Gold"] = 0;
                    }

                    if (!currentData.ContainsKey("Stardust"))
                    {
                        updates["Stardust"] = 0;
                    }

                    if (!currentData.ContainsKey("Stellastone"))
                    {
                        updates["Stellastone"] = 0;
                    }

                    if (!currentData.ContainsKey("OwnedSkins") || currentData["OwnedSkins"] == null)
                    {
                        updates["OwnedSkins"] = new List<string>();
                    }

                    if (!currentData.ContainsKey("OwnedEmotes") || currentData["OwnedEmotes"] == null)
                    {
                        updates["OwnedEmotes"] = new List<string>();
                    }

                    if (!currentData.ContainsKey("OwnedCards") || currentData["OwnedCards"] == null)
                    {
                        updates["OwnedCards"] = new Dictionary<string, int>();
                    }

                    if (!currentData.ContainsKey("Level") || currentData["Level"] == null)
                    {
                        updates["Level"] = 1;
                    }

                    if (!currentData.ContainsKey("Exp") || currentData["Exp"] == null)
                    {
                        updates["Exp"] = 0;
                    }

                    if (!currentData.ContainsKey("Score") || currentData["Score"] == null)
                    {
                        updates["Score"] = 1000;
                    }

                    if (!currentData.ContainsKey("WinCount") || currentData["WinCount"] == null)
                    {
                        updates["WinCount"] = 0;
                    }

                    if (!currentData.ContainsKey("LossCount") || currentData["LossCount"] == null)
                    {
                        updates["LossCount"] = 0;
                    }

                    if (!currentData.ContainsKey("CreateTime") || currentData["CreateTime"] == null)
                    {
                        updates["CreateTime"] = Timestamp.GetCurrentTimestamp();
                    }

                    if (!currentData.ContainsKey("Username") || string.IsNullOrEmpty(currentData["Username"]?.ToString()))
                    {
                        try
                        {
                            UserRecord userRecord = await FirebaseAuth.DefaultInstance.GetUserAsync(uid);
                            updates["Username"] = userRecord.DisplayName ?? (userRecord.Email?.Split('@')[0] ?? "Player");
                        }
                        catch
                        {
                            updates["Username"] = "Player";
                        }
                    }

                    if (updates.Count > 0)
                    {
                        await userDocRef.UpdateAsync(updates);
                        Console.WriteLine($"✅ [UserDataSync] 유저 {uid}의 누락된 필드 자동 보정 완료: {string.Join(", ", updates.Keys)}");
                    }

                    // 기본 덱 존재 여부 확인
                    CollectionReference decksRef = userDocRef.Collection("Decks");
                    QuerySnapshot deckSnapshot = await decksRef.Limit(1).GetSnapshotAsync();
                    if (deckSnapshot.Count == 0)
                    {
                        string initialDeckId = "testDeck_1";
                        DeckData initialDeck = new DeckData
                        {
                            deckId = initialDeckId,
                            deckName = "테스트 덱",
                            deckClass = "임시 직업",
                            cardIds = new List<string>()
                        };
                        await decksRef.Document(initialDeckId).SetAsync(initialDeck);
                        Console.WriteLine($"✅ [UserDataSync] 덱이 없어 기본 덱 자동 생성 완료: {uid}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [UserDataSync] 유저 데이터 검사/보정 중 오류: {ex.Message}");
            }
        }
    }
}
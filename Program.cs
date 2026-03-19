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

    // Firestore에 저장할 사용자 데이터 모델
    [FirestoreData]
    public class UserData
    {
        [FirestoreProperty]
        public string? Username { get; set; }

        [FirestoreProperty]
        public int Level { get; set; }

        [FirestoreProperty]
        public Timestamp CreateTime { get; set; }

        [FirestoreProperty]
        public string? SelectDeck { get; set; }
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

        // Firestore 변환을 위한 기본 생성자
        public DeckData() { }

        // 서버 코드 내에서 객체 생성을 위한 생성자
        public DeckData(string name, string className)
        {
            deckName = name;
            deckClass = className;
            cardIds = new List<string>(); // 새 덱은 항상 비어있는 카드 리스트로 시작
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

            // ==================================================================
            // (신규) 서버 시작 전 카드 데이터베이스 로드
            // ==================================================================
            await ServerCardDatabase.Instance.InitializeAsync(firestoreDb);

            var app = builder.Build();

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
                        CreateTime = Timestamp.GetCurrentTimestamp()
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
            app.MapPost("/api/auth/verify-token", async (IDictionary<string, string> req) =>
            {
                string idToken = req["token"];
                FirebaseToken decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
                string uid = decodedToken.Uid;
                Console.WriteLine($"✅ 토큰 검증 성공, 로그인 유저: {uid}");
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
    }
}
using System;
using System.Threading.Tasks;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GameServer.Endpoints;
using GameServer.Services;

namespace GameServer
{
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
            builder.Services.AddSingleton<ServerMatchmakingService>();
            builder.Services.AddHttpClient();
            Console.WriteLine("✅ Firebase Admin SDK가 성공적으로 초기화되었습니다.");

            // --- 게임 설정 로드 (appsettings.json 단일 소스) ---
            GameServerSettings.EnableSinglePlayerBot = builder.Configuration.GetValue<bool>("GameSettings:EnableSinglePlayerBot", true);
            Console.WriteLine($"⚙️ [GameSettings] 봇 자동 매칭 기본 설정: {GameServerSettings.EnableSinglePlayerBot} (appsettings.json)");

            // 대시보드 전용
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAdminDashboard", policy =>
                {
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                });
            });

            // 대시보드 및 Web API JSON 직렬화 옵션 설정 (필드 포함 및 Enum 문자열 변환)
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.IncludeFields = true;
                options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            });

            // 카드 및 상점 데이터베이스 로드
            await ServerCardDatabase.Instance.InitializeAsync(firestoreDb);
            await ServerProductDatabase.Instance.InitializeAsync(firestoreDb);

            var app = builder.Build();

            // 대시보드 전용
            app.UseCors("AllowAdminDashboard");

            // WebSocket 미들웨어 활성화
            app.UseWebSockets();

            // ==================================================================
            // 도메인별 Minimal API 엔드포인트 등록
            // ==================================================================
            app.MapAuthEndpoints();
            app.MapDeckEndpoints();
            app.MapShopEndpoints();
            app.MapAdminEndpoints();
            app.MapMatchEndpoints();
            app.MapGameSocketEndpoints();

            Console.WriteLine("✅ HTTP 서버가 시작됩니다. Listening on http://*:5123");
            app.Run();
        }

        /// <summary>
        /// Request Header의 Authorization (Bearer 토큰)을 검증하고 UID를 반환합니다.
        /// </summary>
        public static Task<string?> VerifyTokenAsync(string authorization) => AuthHelper.VerifyTokenAsync(authorization);

        /// <summary>
        /// ID 토큰 문자열을 직접 검증하고 UID를 반환합니다. (GameSocketHandler 등에서 호출)
        /// </summary>
        public static Task<string?> VerifyTokenStringAsync(string idToken) => AuthHelper.VerifyTokenStringAsync(idToken);

        /// <summary>
        /// 유저 접속 시 누락된 데이터 보정
        /// </summary>
        public static Task EnsureUserDataIntegrityAsync(string uid, FirestoreDb db) => AuthHelper.EnsureUserDataIntegrityAsync(uid, db);
    }
}
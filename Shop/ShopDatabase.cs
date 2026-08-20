using Google.Cloud.Firestore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameServer.Shop
{
    /// <summary>
    /// Firestore의 Products 컬렉션에서 상품 목록을 로드하고 캐싱하는 데이터베이스 관리자
    /// </summary>
    public class ShopDatabase
    {
        public static ShopDatabase Instance { get; private set; } = new ShopDatabase();

        // Key: 상품 ID (string, 예: "nomal_Card_1000")
        private readonly ConcurrentDictionary<string, ProductData> _productCache = new();

        private ShopDatabase() { }

        /// <summary>
        /// 서버 시작 시 Firestore에서 판매 중인 상품 목록을 로드합니다.
        /// </summary>
        public async Task InitializeAsync(FirestoreDb db)
        {
            Console.WriteLine("[ShopDatabase] 📥 상점 상품 데이터 로딩 시작...");
            try
            {
                CollectionReference productsRef = db.Collection("Products");
                QuerySnapshot snapshot = await productsRef.GetSnapshotAsync();

                _productCache.Clear();

                foreach (DocumentSnapshot doc in snapshot.Documents)
                {
                    ProductData product = doc.ConvertTo<ProductData>();
                    
                    // productId가 비어있으면 문서 ID를 사용
                    if (string.IsNullOrEmpty(product.productId))
                    {
                        product.productId = doc.Id;
                    }

                    if (product.isActive)
                    {
                        _productCache[product.productId] = product;
                    }
                }

                Console.WriteLine($"[ShopDatabase] ✅ 총 {_productCache.Count}개의 활성 상품 로드 완료.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShopDatabase] ❌ 상점 데이터 로딩 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 판매 중인 모든 활성 상품 목록을 반환합니다.
        /// </summary>
        public List<ProductData> GetAllActiveProducts()
        {
            return _productCache.Values
                .OrderBy(p => p.category_Id)
                .ThenBy(p => p.productId)
                .ToList();
        }

        /// <summary>
        /// 특정 상품 ID로 상품 데이터를 조회합니다.
        /// </summary>
        public ProductData? GetProduct(string productId)
        {
            if (_productCache.TryGetValue(productId, out var product))
            {
                return product;
            }
            return null;
        }
    }
}

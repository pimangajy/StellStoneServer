using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameServer.Shop;

namespace GameServer
{
    /// <summary>
    /// 서버 시작 시 Firebase Firestore("Products" 컬렉션)에서 전체 상품 목록을 로드하여
    /// 메모리에 캐싱하고, 클라이언트의 카테고리별 상품 목록 요청을 처리하는 데이터베이스 클래스
    /// </summary>
    public class ServerProductDatabase
    {
        // 싱글톤 인스턴스
        public static ServerProductDatabase Instance { get; private set; } = new ServerProductDatabase();

        // 상품 데이터를 저장할 캐시 딕셔너리 (Key: productId string)
        private Dictionary<string, ProductData> _productCache = new Dictionary<string, ProductData>(StringComparer.OrdinalIgnoreCase);

        private ServerProductDatabase() { }

        /// <summary>
        /// 서버 시작 시 호출되어 Firestore에서 모든 상품 데이터를 로드합니다.
        /// </summary>
        public async Task InitializeAsync(FirestoreDb db)
        {
            Console.WriteLine("[ServerProductDatabase] 🛍️ 상품 데이터 로딩 시작...");
            try
            {
                CollectionReference productsRef = db.Collection("Products");
                QuerySnapshot snapshot = await productsRef.GetSnapshotAsync();

                _productCache.Clear();

                if (snapshot.Documents.Count == 0)
                {
                    Console.WriteLine("[ServerProductDatabase] ⚠️ Firestore 'Products' 컬렉션에 등록된 상품이 없습니다.");
                    return;
                }

                // Firestore 문서들을 메모리에 캐싱
                foreach (DocumentSnapshot document in snapshot.Documents)
                {
                    try
                    {
                        ProductData product = document.ConvertTo<ProductData>();

                        // productId 필드가 비어있으면 문서 ID를 상품 ID로 사용
                        if (string.IsNullOrWhiteSpace(product.productId))
                        {
                            product.productId = document.Id;
                        }

                        if (!string.IsNullOrEmpty(product.productId))
                        {
                            _productCache[product.productId] = product;            
                        }
                    }
                    catch (Exception docEx)
                    {
                        Console.WriteLine($"[ServerProductDatabase] ⚠️ 상품 문서({document.Id}) 변환 중 오류: {docEx.Message}");
                    }
                }

                Console.WriteLine($"[ServerProductDatabase] ✅ 총 {_productCache.Count}개의 상점 상품 로드 완료.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServerProductDatabase] ❌ 상품 데이터 로딩 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 전체 상품 목록을 반환합니다.
        /// </summary>
        public List<ProductData> GetAllProducts()
        {
            return _productCache.Values.ToList();
        }

        /// <summary>
        /// 카테고리별로 활성화된(isActive == true) 상품 목록을 반환합니다.
        /// categoryId가 null이면 모든 활성 상품을 반환합니다.
        /// </summary>
        public List<ProductData> GetProducts(int? categoryId = null)
        {
            var query = _productCache.Values.Where(p => p.isActive);

            if (categoryId.HasValue)
            {
                query = query.Where(p => (int)p.category_Id == categoryId.Value);
            }

            return query.ToList();
        }

        /// <summary>
        /// 특정 상품 ID(문자열)의 상품 데이터를 조회합니다.
        /// </summary>
        public ProductData? GetProduct(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return null;

            if (_productCache.TryGetValue(productId, out ProductData? product))
            {
                return product;
            }
            return null;
        }
    }
}

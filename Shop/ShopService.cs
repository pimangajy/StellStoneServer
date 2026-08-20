using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameServer.Shop
{
    /// <summary>
    /// 상점 구매 처리, 재화 차감, 트랜잭션 및 카드/보상 지급 비즈니스 로직을 담당하는 서비스
    /// </summary>
    public class ShopService
    {
        public static ShopService Instance { get; private set; } = new ShopService();

        private ShopService() { }

        /// <summary>
        /// 유저의 상품 구매 요청을 처리합니다 (트랜잭션 기반).
        /// </summary>
        public async Task<PurchaseResponse> PurchaseProductAsync(string uid, string productId, int amount, FirestoreDb db)
        {
            // 상품 존재 여부 및 활성화 확인
            ProductData? product = ShopDatabase.Instance.GetProduct(productId);
            if (product == null || !product.isActive)
            {
                return new PurchaseResponse
                {
                    success = false,
                    message = "판매 중이지 않거나 존재하지 않는 상품입니다."
                };
            }

            // TODO: Firestore 트랜잭션(재화 차감 -> 보상 지급) 구현 예정
            return await Task.FromResult(new PurchaseResponse
            {
                success = false,
                message = "구매 처리 로직 준비 중입니다."
            });
        }
    }
}

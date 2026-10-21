using System.Collections.Generic;
using CatHealingHouse.Furniture;

namespace CatHealingHouse.Cats
{
    /// <summary>
    /// 고양이 성격. 같은 FSM 을 쓰면서도 고양이마다 다르게 보이게 하는 배율과 취향 묶음.
    /// 1.0 이 보통. 나중에 ScriptableObject 로 옮겨 디자이너가 표로 관리한다.
    /// </summary>
    public sealed class CatPersonality
    {
        public string Id = "normal";
        public string DisplayName = "보통";
        public string Description = "";

        // 움직임
        public double MoveSpeed = 1.0;   // 클수록 빨리 걷는다
        public double IdleScale = 1.0;   // 클수록 오래 멍때린다
        public double WanderScale = 1.0; // 클수록 한 번에 멀리 산책
        public double GroomChance = -1;  // 음수면 기본값 사용
        public double SunLove = 1.0;     // 햇빛 쬐기를 얼마나 좋아하는지 (멍때릴 때 햇빛 칸을 고를 확률 배율)

        // 욕구별 배율: 감소 속도와 사용 중 회복 속도
        public readonly Dictionary<NeedType, double> DecayScale = new Dictionary<NeedType, double>();
        public readonly Dictionary<NeedType, double> RefillScale = new Dictionary<NeedType, double>();

        // 좋아하는 가구 (가구 정의 ID → 애정도). 1 = 보통, 2 = 좋아함, 3 = 아주 좋아함.
        public readonly Dictionary<string, double> FurnitureLove = new Dictionary<string, double>();

        public double Decay(NeedType t) => DecayScale.TryGetValue(t, out var v) ? v : 1.0;
        public double Refill(NeedType t) => RefillScale.TryGetValue(t, out var v) ? v : 1.0;
        public double Love(FurnitureDef def) => def != null && FurnitureLove.TryGetValue(def.Id, out var v) ? v : 1.0;

        public static CatPersonality Normal() => new CatPersonality();
    }

    /// <summary>기본 성격 프리셋. 이름과 수치는 기획 단계 초안.</summary>
    public static class CatPersonalities
    {
        /// <summary>잠이 많고 느릿느릿. 침대와 방석을 좋아하고 한번 자면 오래 잔다.</summary>
        public static CatPersonality Sleepyhead()
        {
            var p = new CatPersonality
            {
                Id = "sleepyhead", DisplayName = "잠꾸러기",
                Description = "틈만 나면 잔다. 한번 누우면 일어날 생각이 없다.",
                MoveSpeed = 0.7, IdleScale = 1.8, WanderScale = 0.6, GroomChance = 0.25, SunLove = 2.2,
            };
            p.DecayScale[NeedType.Energy] = 2.6;
            p.RefillScale[NeedType.Energy] = 0.35;
            p.FurnitureLove[FurnitureCatalog.Bed.Id] = 3;
            p.FurnitureLove[FurnitureCatalog.Cushion.Id] = 2;
            return p;
        }

        /// <summary>높은 곳 대장. 캣타워에 한번 올라가면 오래 머문다.</summary>
        public static CatPersonality TowerKing()
        {
            var p = new CatPersonality
            {
                Id = "tower_king", DisplayName = "높은 곳 대장",
                Description = "캣타워 꼭대기는 내 자리. 내려다보는 게 제일 좋다.",
                MoveSpeed = 1.0, IdleScale = 1.0, WanderScale = 0.9,
            };
            p.DecayScale[NeedType.Fun] = 1.2;
            p.FurnitureLove[FurnitureCatalog.CatTower3.Id] = 3;
            p.FurnitureLove[FurnitureCatalog.CatTower5.Id] = 3;
            return p;
        }

        /// <summary>에너자이저. 빨리 걷고 쉬지 않으며 캣휠을 자주 탄다.</summary>
        public static CatPersonality Energizer()
        {
            var p = new CatPersonality
            {
                Id = "energizer", DisplayName = "에너자이저",
                Description = "가만히 있지를 못한다. 캣휠 위에서 하루를 보낸다.",
                MoveSpeed = 1.35, IdleScale = 0.45, WanderScale = 1.6, GroomChance = 0.05, SunLove = 0.4,
            };
            p.DecayScale[NeedType.Fun] = 1.7;
            p.DecayScale[NeedType.Energy] = 0.7;
            p.FurnitureLove[FurnitureCatalog.CatWheel.Id] = 3;
            p.FurnitureLove[FurnitureCatalog.Scratcher.Id] = 1.5;
            return p;
        }

        /// <summary>먹보. 배가 금방 고프고 밥그릇 근처를 맴돈다.</summary>
        public static CatPersonality Foodie()
        {
            var p = new CatPersonality
            {
                Id = "foodie", DisplayName = "먹보",
                Description = "밥그릇 소리만 들려도 달려온다.",
                MoveSpeed = 0.9, IdleScale = 1.2, WanderScale = 0.8, SunLove = 1.3,
            };
            p.DecayScale[NeedType.Hunger] = 2.2;
            p.RefillScale[NeedType.Hunger] = 0.6; // 천천히 오래 먹는다
            p.FurnitureLove[FurnitureCatalog.FoodBowl.Id] = 2.5;
            return p;
        }

        /// <summary>쫄보. 숨숨집과 박스 안을 좋아하고 오래 숨어 있다.</summary>
        public static CatPersonality Shy()
        {
            var p = new CatPersonality
            {
                Id = "shy", DisplayName = "쫄보",
                Description = "좁고 어두운 곳이 안전하다. 숨숨집에서 얼굴만 내민다.",
                MoveSpeed = 0.85, IdleScale = 1.4, WanderScale = 0.5, GroomChance = 0.2, SunLove = 0.7,
            };
            p.FurnitureLove[FurnitureCatalog.HidingHouse.Id] = 3;
            p.FurnitureLove[FurnitureCatalog.BoxSmall.Id] = 2.5;
            p.FurnitureLove[FurnitureCatalog.BoxDouble.Id] = 2;
            return p;
        }

        /// <summary>호기심쟁이. 쉬지 않고 방 구석구석을 돌아다닌다.</summary>
        public static CatPersonality Curious()
        {
            var p = new CatPersonality
            {
                Id = "curious", DisplayName = "호기심쟁이",
                Description = "새 가구가 생기면 제일 먼저 냄새를 맡으러 온다.",
                MoveSpeed = 1.15, IdleScale = 0.7, WanderScale = 2.0, GroomChance = 0.08,
            };
            p.DecayScale[NeedType.Fun] = 1.3;
            p.FurnitureLove[FurnitureCatalog.Scratcher.Id] = 2;
            return p;
        }

        public static IReadOnlyList<System.Func<CatPersonality>> All = new System.Func<CatPersonality>[]
        {
            Sleepyhead, TowerKing, Energizer, Foodie, Shy, Curious,
        };

        /// <summary>저장된 성격 ID 로 다시 만든다. 모르면 보통.</summary>
        public static CatPersonality ById(string id)
        {
            foreach (var make in All) { var p = make(); if (p.Id == id) return p; }
            return CatPersonality.Normal();
        }
    }
}

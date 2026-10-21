using System;
using CatHealingHouse.Furniture;

namespace CatHealingHouse.Cats
{
    /// <summary>
    /// 고양이 행동 수치. 2단계 튜닝 대상 (기획서: 배회 확률, 욕구 임계치, 행동 지속시간).
    /// 지금은 확인이 쉽도록 빠르게 잡아뒀다. 나중에 ScriptableObject 로 옮긴다.
    /// </summary>
    [Serializable]
    public sealed class CatTuning
    {
        // 이동
        public double HopSeconds = 0.75;

        // RCT식 배회: 방향 가중치 (직진 / 60도 / 120도). 되돌아가기는 막다른 곳에서만.
        public double StraightWeight = 60;
        public double Turn60Weight = 15;   // 좌우 각각
        public double Turn120Weight = 5;   // 좌우 각각
        public int WanderMinSteps = 2, WanderMaxSteps = 6;
        public double IdleMinSeconds = 3.0, IdleMaxSeconds = 7.0;

        // 멍때리기: 가만히 있다가 다음 행동을 고를 때 (그루밍 다음) 이 확률로 멍때린다.
        // 성격의 IdleScale 에 따라 늘거나 줄어든다 (잠꾸러기는 자주, 에너자이저는 가끔).
        public double LoungeChance = 0.55;
        public double LoungeFurnitureChance = 0.55; // 그중 캣타워·숨숨집·박스·방석 등에 가서 멍때릴 확률
        public double LieChance = 0.45;             // 누워서 멍 (나머지는 앉아서 멍)
        public double LoungeMinSeconds = 12, LoungeMaxSeconds = 35;
        public double LieEnergyPerSecond = 0.8;     // 누워 있으면 에너지가 조금씩 찬다

        // 햇빛 쬐기: 멍때릴 때 해가 떠 있으면 이 확률(× 성격 SunLove)로 창문 빛 칸을 찾아간다.
        // 빛이 옮겨가면 가까운 빛 칸으로 따라 옮긴다.
        public double SunbathChance = 0.55;
        public double SunLoungeMinSeconds = 45, SunLoungeMaxSeconds = 110;
        public double SunMoodPerSecond = 0.05;
        public int SunSearchRadius = 9;
        public int SunFollowRadius = 4;
        public double GroomChance = 0.15;
        public double GroomSeconds = 3.0;

        // 욕구: 이 값 아래로 떨어지면 가구를 찾아간다
        public double NeedThreshold = 45;
        public double SatisfiedLevel = 95;

        // 초당 감소량
        public double HungerDecay = 0.28;
        public double BladderDecay = 0.22;
        public double EnergyDecay = 0.15;
        public double FunDecay = 0.32;

        // 사용 중 초당 회복량
        public double HungerRefill = 8;
        public double BladderRefill = 15;
        public double EnergyRefill = 4;
        public double FunRefill = 6;

        // 성격: 좋아하는 가구
        public double FavoriteThresholdBonus = 15; // 애정도 1점당 임계치 +15 (더 자주 감)
        public double FavoriteThresholdCap = 80;
        public double LingerPerLove = 10;          // 애정도 1점당 최소 10초 더 머물기 (더 오래 있음)

        public double EnterSeconds = 0.5;
        public double ExitSeconds = 0.5;
        public double MaxUseSeconds = 40;
        public double RetargetCooldownSeconds = 3;

        public double Decay(NeedType t)
        {
            switch (t)
            {
                case NeedType.Hunger: return HungerDecay;
                case NeedType.Bladder: return BladderDecay;
                case NeedType.Energy: return EnergyDecay;
                case NeedType.Fun: return FunDecay;
                default: return 0;
            }
        }

        public double Refill(NeedType t)
        {
            switch (t)
            {
                case NeedType.Hunger: return HungerRefill;
                case NeedType.Bladder: return BladderRefill;
                case NeedType.Energy: return EnergyRefill;
                case NeedType.Fun: return FunRefill;
                default: return 0;
            }
        }
    }
}

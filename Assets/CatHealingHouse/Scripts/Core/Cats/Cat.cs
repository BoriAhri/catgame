using System;
using CatHealingHouse.Furniture;
using CatHealingHouse.World;

namespace CatHealingHouse.Cats
{
    public enum Affiliation { Stray, Resident }

    /// <summary>길냥이 행동 상태 (기획서). 화면용 CatState 는 여기서 골라 넣는다.</summary>
    public enum StrayState { None, Approaching, Waiting, Eating, ToLoaf, Loafing, ToDoor, Peeking, ReadyToAdopt, Leaving, Fleeing, Adopting }

    /// <summary>기척에 대한 반응 표시 (머리 위 아이콘).</summary>
    public enum StrayReaction { None, Alert, Curious, Love }

    /// <summary>실내 고양이 행동 상태. 가구 사용은 Entering → Using → Exiting 세 구간으로 나눈다.</summary>
    public enum CatState { Idle, Wandering, MovingToTarget, Entering, Using, Exiting, Grooming, Lounging, ToSpot, Acting }

    /// <summary>짧은 몸짓 (Acting 상태에서): 기지개 → 하품, 뒷발로 귀 긁기.</summary>
    public enum CatAct { None, Stretch, Yawn, EarScratch }

    /// <summary>멍때리기 자세. 욕구와 상관없이 그냥 쉬는 것 (빈칸 또는 캣타워·숨숨집 등 위에서).</summary>
    public enum LoungeKind { None, Sit, Lie, Sprawl }

    /// <summary>심즈식 욕구: 100 = 충족, 0 = 결핍.</summary>
    [Serializable]
    public sealed class CatNeeds
    {
        public double Hunger = 100;
        public double Bladder = 100;
        public double Energy = 100;
        public double Fun = 100;

        public double Get(NeedType t)
        {
            switch (t)
            {
                case NeedType.Hunger: return Hunger;
                case NeedType.Bladder: return Bladder;
                case NeedType.Energy: return Energy;
                case NeedType.Fun: return Fun;
                default: return 100;
            }
        }

        public void Set(NeedType t, double v)
        {
            v = Clamp(v);
            switch (t)
            {
                case NeedType.Hunger: Hunger = v; break;
                case NeedType.Bladder: Bladder = v; break;
                case NeedType.Energy: Energy = v; break;
                case NeedType.Fun: Fun = v; break;
            }
        }

        public void Add(NeedType t, double delta) => Set(t, Get(t) + delta);

        public static readonly NeedType[] All = { NeedType.Hunger, NeedType.Bladder, NeedType.Energy, NeedType.Fun };

        private static double Clamp(double v) => v < 0 ? 0 : v > 100 ? 100 : v;
    }

    /// <summary>
    /// 고양이 한 마리의 런타임 상태. 정적 프로필(성격, 취미 등)은 나중 단계에서 붙인다.
    /// 이동은 칸 단위 점프이고, 화면은 PrevCell → Cell 사이를 HopProgress 로 보간해서 그린다.
    /// </summary>
    public sealed class Cat
    {
        public readonly int Id;
        public string Name;
        public Affiliation Affiliation = Affiliation.Resident;
        public readonly CatNeeds Needs = new CatNeeds();
        public CatPersonality Personality = CatPersonality.Normal();
        /// <summary>같은 성격이어도 조금씩 다르게: 개체별 속도 편차 (0.9~1.1).</summary>
        public double SpeedJitter = 1.0;

        /// <summary>기분 게이지 0~100 (100 = 행복). 화장실 청결도 등으로 오르내린다.</summary>
        public double Mood = 70;

        public void AddMood(double delta) => Mood = Math.Max(0, Math.Min(100, Mood + delta));

        // ───── 길냥이 전용 ─────
        public int StrayId;
        public StrayState StrayState;
        public double StrayTimer;
        public int StrayRetries;
        public StrayReaction Reaction;
        public double ReactionTimer;
        public double AlertAccum;
        public bool AteThisVisit;
        /// <summary>이번 방문에 이미 한 행동 (간식·놀아주기·쓰다듬기 비트).</summary>
        public int VisitFlags;
        /// <summary>지금 하는 교감 동작 (0 = 없음, 그 외는 StrayAction + 1). 화면 몸짓용.</summary>
        public int Activity;
        public double ActivityTimer;
        /// <summary>터치 메뉴가 열려 있는 동안 제자리에 멈춰 있다.</summary>
        public bool Held;

        // 위치와 이동
        public Cell Cell;            // 논리상 현재 칸 (점프를 시작하면 바로 목적 칸이 된다)
        public Cell PrevCell;        // 화면 보간용 출발 칸
        public double HopProgress = 1; // 0~1, 1 이면 멈춰 있음
        public double HopDuration = 0.45;
        public int Heading;          // 0~5, 마지막으로 움직인 방향

        // 상태
        public CatState State = CatState.Idle;
        public double StateTimer;
        public int WanderStepsLeft;
        public double RetargetCooldown;

        // 목표 가구
        public int TargetFurnitureId = -1;
        public int SlotIndex = -1;

        /// <summary>입고 있는 코스튬 ID (공방에서 제작, 없으면 null).</summary>
        public string Costume;

        // 가까이 보기 쓰다듬기 (Petting)
        /// <summary>쓰다듬기 피로 (PetFatigueAt 시각 기준 값, 시간이 지나면 풀린다).</summary>
        public double PetFatigue;
        public double PetFatigueAt;
        /// <summary>이 시각(SimTime)부터 다시 젤리 하트를 준다.</summary>
        public double PetHeartAt = double.NegativeInfinity;

        // 친밀도 (Intimacy)
        /// <summary>친밀도 레벨 1~99.</summary>
        public int IntimacyLevel = 1;
        /// <summary>지금 레벨 안에서 모은 경험치.</summary>
        public double IntimacyExp;
        /// <summary>쓰지 않은 스킬 포인트 (레벨업마다 1).</summary>
        public int SkillPoints;
        /// <summary>간식 하루 횟수 세기 (SnackDay 날짜에 SnacksToday 번).</summary>
        public int SnackDay, SnacksToday;
        /// <summary>패시브 스킬 레벨 (CatSkills 의 Id → 레벨).</summary>
        public readonly System.Collections.Generic.Dictionary<string, int> Skills = new System.Collections.Generic.Dictionary<string, int>();


        /// <summary>겉모습 열쇠 (저장·불러오기 때 같은 그림을 다시 고르려고). 화면 쪽에서 정한다.</summary>
        public string LookKey;
        public NeedType TargetNeed = NeedType.None;
        public double UseTime;

        // 멍때리기
        public LoungeKind Lounge;
        public double LoungeTimer;
        /// <summary>지금 가구에 간 이유가 욕구가 아니라 멍때리기다 (보상 없음).</summary>
        public bool LoungeVisit;
        /// <summary>창문 햇빛 속에서 멍때리는 중 (빛이 옮겨가면 따라간다).</summary>
        public bool Sunbathing;
        /// <summary>지금 하는 짧은 몸짓 (State == Acting 일 때).</summary>
        public CatAct Act;
        /// <summary>우다다 중 (산책이 빠르고 길어진다).</summary>
        public bool Zoomies;
        /// <summary>누워 있는 멍 (식빵 눕기 또는 옆으로 길게).</summary>
        public bool IsLyingDown => Lounge == LoungeKind.Lie || Lounge == LoungeKind.Sprawl;
        /// <summary>ToSpot 상태에서 남은 경로 (햇빛 칸까지).</summary>
        public System.Collections.Generic.List<Cell> SpotPath;

        public Cat(int id, string name, Cell start)
        {
            Id = id;
            Name = name;
            Cell = start;
            PrevCell = start;
        }

        public bool IsHopping => HopProgress < 1;
        public bool HasTarget => TargetFurnitureId > 0;
        public bool IsInsideFurniture => State == CatState.Entering || State == CatState.Using || State == CatState.Exiting;

        public override string ToString() => $"{Name}#{Id} {State} @{Cell}";
    }
}

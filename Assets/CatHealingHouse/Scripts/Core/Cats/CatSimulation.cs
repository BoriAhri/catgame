using System;
using System.Collections.Generic;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.World;

namespace CatHealingHouse.Cats
{
    /// <summary>
    /// 고양이들의 두뇌와 이동. 고정 틱으로 돌고, 화면이 꺼져 있어도 그대로 진행된다.
    ///
    /// 흐름 (RCT 방식):
    ///   급한 욕구 없음 → 직진을 좋아하는 6방향 배회, 가끔 멈춤·그루밍
    ///   욕구가 임계치 아래 → 그 욕구를 채우는 가구 중 빈 슬롯이 있는 것을 골라 슬롯 예약
    ///   → 거리맵에서 숫자가 작은 쪽으로 한 칸씩 → 들어가기 → 사용 → 나오기 → 슬롯 반납
    ///
    /// 성격 (CatPersonality):
    ///   걷는 속도, 멍때리는 시간, 산책 길이, 그루밍 확률, 욕구별 감소·회복 배율이 고양이마다 다르다.
    ///   좋아하는 가구는 더 자주 (임계치 상향), 더 오래 (최소 머무는 시간), 멀어도 (거리 ÷ 애정도) 간다.
    /// </summary>
    public sealed class CatSimulation
    {
        public const double FixedStep = 0.1;

        public readonly HouseWorld World;
        public readonly DistanceMapCache Maps;
        public readonly CatTuning Tuning;

        private readonly Random _rng;
        private readonly List<Cat> _cats = new List<Cat>();
        private double _accumulator;
        private int _nextCatId = 1;

        public IReadOnlyList<Cat> Cats => _cats;
        public double SimTime { get; private set; }

        /// <summary>가구 사용을 끝까지 마쳤을 때. 3단계에서 트레이 결과물과 하트를 여기에 붙인다.</summary>
        public event Action<Cat, FurnitureInstance> InteractionCompleted;

        /// <summary>고양이가 한 칸 움직이기 시작했을 때 (바닥 드롭 판정용).</summary>
        public event Action<Cat> CatHopped;

        /// <summary>고정 틱 하나가 끝났을 때. 경제 시스템이 같은 시계로 돌게 한다.</summary>
        public event Action<double> Stepped;

        /// <summary>가구 선택 점수에 더할 추가 비용 (예: 더러운 화장실은 피한다).</summary>
        public Func<Cat, FurnitureInstance, double> ExtraCost;

        /// <summary>길냥이 두뇌. 설정돼 있으면 Stray 고양이는 이 함수가 움직인다 (걸음 진행은 시뮬레이션이 처리).</summary>
        public Action<Cat, double> StrayBrain;

        /// <summary>이 칸에 창문 햇빛이 들어오는지 (환경 시스템이 설정). 없으면 햇빛 쬐기 없음.</summary>
        public Func<Cell, bool> IsSunny;

        /// <summary>지금 밤인지 (환경 시스템이 설정). 밤에는 우다다가 잦다.</summary>
        public Func<bool> IsNight;

        /// <summary>놀아주기 두뇌 (PlaySystem). 집 고양이마다 걸음 중이 아닐 때 불리고, true 면 그 틱은 놀이가 맡는다.</summary>
        public Func<Cat, double, bool> PlayBrain;

        public event Action<Cat> CatAdded;
        public event Action<Cat> CatRemoved;

        public CatSimulation(HouseWorld world, DistanceMapCache maps, CatTuning tuning = null, int seed = 12345)
        {
            World = world;
            Maps = maps;
            Tuning = tuning ?? new CatTuning();
            _rng = new Random(seed);
        }

        public Cat AddCat(string name, Cell at, CatPersonality personality = null)
        {
            if (!World.IsTransit(at)) throw new ArgumentException($"{at} 는 고양이가 설 수 없는 칸입니다.");
            var cat = new Cat(_nextCatId++, name, at)
            {
                Heading = _rng.Next(6),
                Personality = personality ?? CatPersonality.Normal(),
                SpeedJitter = Range(0.9, 1.1),
            };
            cat.StateTimer = Range(0.2, 1.5);
            _cats.Add(cat);
            CatAdded?.Invoke(cat);
            return cat;
        }

        /// <summary>길냥이를 마당 테두리 같은 칸에 등장시킨다. 설 수 있는 칸이면 문만 아니면 된다.</summary>
        public Cat AddStray(string name, Cell at, int strayId)
        {
            var cat = new Cat(_nextCatId++, name, at)
            {
                Affiliation = Affiliation.Stray,
                Heading = _rng.Next(6),
                SpeedJitter = Range(0.9, 1.1),
                StrayId = strayId,
            };
            _cats.Add(cat);
            CatAdded?.Invoke(cat);
            return cat;
        }

        public void RemoveCat(Cat cat)
        {
            if (cat.HasTarget) ReleaseTarget(cat);
            if (_cats.Remove(cat)) CatRemoved?.Invoke(cat);
        }

        /// <summary>바깥(길냥이 두뇌 등)에서 고양이를 한 칸 움직인다. durationScale 이 크면 느리게.</summary>
        public void Hop(Cat cat, Cell next, double durationScale = 1)
        {
            StartHop(cat, next);
            cat.HopDuration *= durationScale;
        }

        /// <summary>길냥이 영입: 실내 고양이로 바꾼다. 이후로는 일반 FSM 이 움직인다.</summary>
        public void MakeResident(Cat cat, CatPersonality personality)
        {
            if (cat.HasTarget) ReleaseTarget(cat);
            cat.LoungeVisit = false;
            EndLounge(cat);
            cat.Affiliation = Affiliation.Resident;
            cat.Personality = personality ?? CatPersonality.Normal();
            cat.StrayState = StrayState.None;
            cat.Reaction = StrayReaction.None;
            foreach (var n in CatNeeds.All) cat.Needs.Set(n, 80);
            cat.Mood = 90;
            EnterIdle(cat, 0.5, 1.5);
        }

        public void ReserveFor(Cat cat, FurnitureInstance f, NeedType need)
        {
            if (cat.HasTarget) ReleaseTarget(cat);
            cat.SlotIndex = f.ReserveSlot(cat.Id);
            cat.TargetFurnitureId = f.Id;
            cat.TargetNeed = need;
        }

        public void ReleaseFor(Cat cat) => ReleaseTarget(cat);

        /// <summary>실제 경과 시간을 넣으면 고정 틱으로 잘라서 진행한다.</summary>
        public void Advance(double dt)
        {
            _accumulator += dt;
            int guard = 0;
            while (_accumulator >= FixedStep && guard++ < 10000)
            {
                Step(FixedStep);
                _accumulator -= FixedStep;
            }
        }

        public void Step(double dt)
        {
            SimTime += dt;
            // 길냥이 두뇌가 고양이를 지울 수 있으니 복사본으로 돈다
            var snapshot = _cats.ToArray();
            for (int i = 0; i < snapshot.Length; i++) if (_cats.Contains(snapshot[i])) UpdateCat(snapshot[i], dt);
            for (int i = 0; i < _cats.Count; i++) DriftMood(_cats[i], dt);
            Stepped?.Invoke(dt);
        }

        /// <summary>좋아하는 가구는 덜 급해도 간다: 애정도 1점당 임계치 상향 (상한 있음).</summary>
        public double EffectiveThreshold(Cat cat, FurnitureDef def)
        {
            double love = cat.Personality.Love(def);
            if (love <= 1) return Tuning.NeedThreshold;
            return Math.Min(Tuning.FavoriteThresholdCap, Tuning.NeedThreshold + Tuning.FavoriteThresholdBonus * (love - 1));
        }

        /// <summary>좋아하는 가구에서는 다 채워져도 최소 이만큼은 머문다.</summary>
        public double LingerSeconds(Cat cat, FurnitureDef def)
        {
            double love = cat.Personality.Love(def);
            return love > 1 ? Tuning.LingerPerLove * (love - 1) : 0;
        }

        // ───────────────────────── 고양이 한 마리 ─────────────────────────

        private void UpdateCat(Cat cat, double dt)
        {
            if (cat.Affiliation == Affiliation.Stray)
            {
                if (cat.IsHopping)
                {
                    cat.HopProgress += dt / cat.HopDuration;
                    if (cat.HopProgress >= 1) cat.HopProgress = 1;
                }
                // 두뇌는 매 틱 불린다. 걸음 중(IsHopping)이면 기척 반응만 처리하고 이동 결정은 건너뛴다.
                StrayBrain?.Invoke(cat, dt);
                return;
            }

            DecayNeeds(cat, dt);
            if (cat.RetargetCooldown > 0) cat.RetargetCooldown -= dt;

            if (cat.IsHopping)
            {
                cat.HopProgress += dt / cat.HopDuration;
                if (cat.HopProgress < 1) return;
                cat.HopProgress = 1;
            }
            // 가까이 보기로 쓰다듬는 동안은 제자리 (욕구는 그대로 줄어든다)
            if (cat.Held) return;
            if (PlayBrain != null && PlayBrain(cat, dt)) return;

            switch (cat.State)
            {
                case CatState.Idle:
                    cat.StateTimer -= dt;
                    if (TryChooseTarget(cat)) return;
                    if (cat.StateTimer > 0) return;
                    double groom = cat.Personality.GroomChance >= 0 ? cat.Personality.GroomChance : Tuning.GroomChance;
                    double lounge = Math.Min(0.8, Tuning.LoungeChance * Math.Sqrt(Math.Max(0.1, cat.Personality.IdleScale)));
                    double roll = _rng.NextDouble();
                    if (roll < groom)
                    {
                        if (_rng.NextDouble() < Tuning.EarScratchChance) StartAct(cat, CatAct.EarScratch);
                        else
                        {
                            cat.State = CatState.Grooming;
                            cat.StateTimer = Tuning.GroomSeconds;
                        }
                    }
                    // 급한 욕구가 있는데 채울 가구가 없으면 멍때리지 않고 서성인다
                    else if (roll < groom + (1 - groom) * lounge && !AnyUrgent(cat)) StartLounge(cat);
                    else StartWander(cat, mayZoom: true);
                    return;

                case CatState.Lounging:
                    cat.LoungeTimer -= dt;
                    if (cat.IsLyingDown) cat.Needs.Add(NeedType.Energy, Tuning.LieEnergyPerSecond * dt);
                    // 급한 욕구가 생기면 멍을 멈추고 간다
                    if (AnyUrgent(cat) && TryChooseTarget(cat)) { EndLounge(cat); return; }
                    if (cat.LoungeTimer <= 0)
                    {
                        bool lay = cat.IsLyingDown;
                        EndLounge(cat);
                        EnterIdle(cat, 0.5, 1.5);
                        // 누워 있다 일어나면 가끔 기지개 쭉
                        if (lay && _rng.NextDouble() < Tuning.LieStretchChance) StartAct(cat, CatAct.Stretch);
                        return;
                    }
                    if (cat.Sunbathing)
                    {
                        cat.AddMood(Tuning.SunMoodPerSecond * dt);
                        // 햇빛이 옮겨갔으면 가까운 빛 칸으로 따라간다. 너무 멀면 그만 일어난다.
                        if (IsSunny == null || !IsSunny(cat.Cell))
                            if (!TryGoToSun(cat, Tuning.SunFollowRadius)) { EndLounge(cat); EnterIdle(cat, 0.5, 1.5); }
                    }
                    return;

                case CatState.ToSpot:
                    if (AnyUrgent(cat) && TryChooseTarget(cat)) { EndLounge(cat); return; }
                    if (cat.SpotPath == null || cat.SpotPath.Count == 0)
                    {
                        // 도착: 빛이 아직 있으면 눌러앉고, 그사이 빛이 떠났으면 다시 찾는다
                        bool taken = false;
                        foreach (var o in _cats) if (o != cat && o.Sunbathing && o.State == CatState.Lounging && o.Cell == cat.Cell) taken = true;
                        if (IsSunny != null && IsSunny(cat.Cell) && !taken) cat.State = CatState.Lounging;
                        else if (!TryGoToSun(cat, Tuning.SunFollowRadius)) { EndLounge(cat); EnterIdle(cat, 0.5, 1.5); }
                        return;
                    }
                    var nextSpot = cat.SpotPath[0];
                    cat.SpotPath.RemoveAt(0);
                    if (!CanWanderInto(cat, nextSpot)) { EndLounge(cat); EnterIdle(cat, 0.5, 1.5); return; }
                    StartHop(cat, nextSpot);
                    return;

                case CatState.Wandering:
                    if (TryChooseTarget(cat)) return;
                    if (cat.WanderStepsLeft <= 0 || !WanderStep(cat)) EnterIdle(cat);
                    return;

                case CatState.Grooming:
                    cat.StateTimer -= dt;
                    if (cat.StateTimer <= 0) EnterIdle(cat);
                    return;

                case CatState.Acting:
                    cat.StateTimer -= dt;
                    if (AnyUrgent(cat) && TryChooseTarget(cat)) return;
                    if (cat.StateTimer > 0) return;
                    // 기지개 다음엔 하품
                    if (cat.Act == CatAct.Stretch) { StartAct(cat, CatAct.Yawn); return; }
                    EnterIdle(cat, 0.5, 1.5);
                    return;

                case CatState.MovingToTarget:
                    UpdateMoving(cat);
                    return;

                case CatState.Playing:
                    // 놀이 두뇌가 놓아준 고양이 (보통은 그쪽에서 정리한다)
                    EnterIdle(cat, 0.5, 1.5);
                    return;

                case CatState.Entering:
                    cat.StateTimer -= dt;
                    if (cat.StateTimer > 0) return;
                    cat.State = CatState.Using;
                    cat.UseTime = 0;
                    return;

                case CatState.Using:
                {
                    var f = World.GetFurniture(cat.TargetFurnitureId);
                    if (f == null) { BeginExit(cat); return; }
                    if (cat.LoungeVisit)
                    {
                        // 가구 위에서 멍: 정해진 시간이 끝나거나 급한 욕구가 생기면 내려온다
                        cat.UseTime += dt;
                        if (cat.IsLyingDown || f.Def.Interaction == InteractionType.Enter)
                            cat.Needs.Add(NeedType.Energy, Tuning.LieEnergyPerSecond * dt);
                        if (cat.UseTime >= cat.LoungeTimer || AnyUrgent(cat)) BeginExit(cat);
                        return;
                    }
                    cat.Needs.Add(cat.TargetNeed, Tuning.Refill(cat.TargetNeed) * cat.Personality.Refill(cat.TargetNeed) * f.Def.Power * dt);
                    cat.UseTime += dt;
                    double linger = LingerSeconds(cat, f.Def);
                    bool satisfied = cat.Needs.Get(cat.TargetNeed) >= Tuning.SatisfiedLevel && cat.UseTime >= linger;
                    if (satisfied || cat.UseTime >= Tuning.MaxUseSeconds + linger) BeginExit(cat);
                    return;
                }

                case CatState.Exiting:
                    cat.StateTimer -= dt;
                    if (cat.StateTimer > 0) return;
                    FinishExit(cat);
                    return;
            }
        }

        private void DecayNeeds(Cat cat, double dt)
        {
            foreach (var n in CatNeeds.All)
            {
                if (cat.State == CatState.Using && n == cat.TargetNeed) continue;
                cat.Needs.Add(n, -Tuning.Decay(n) * cat.Personality.Decay(n) * dt);
            }
        }

        // ───────────────────────── 목적지 ─────────────────────────

        /// <summary>
        /// 급한 욕구부터 보고, 채워줄 가구 중 빈 슬롯이 있는 것을 예약한다.
        /// 좋아하는 가구는 임계치가 높아서(덜 급해도) 더 자주 가고, 멀어도 고른다 (점수 = 거리 ÷ 애정도).
        /// </summary>
        private bool TryChooseTarget(Cat cat)
        {
            if (cat.HasTarget || cat.RetargetCooldown > 0) return false;

            var needs = new List<NeedType>(CatNeeds.All);
            needs.Sort((a, b) => cat.Needs.Get(a).CompareTo(cat.Needs.Get(b)));
            bool anyUrgent = false;

            foreach (var need in needs)
            {
                double value = cat.Needs.Get(need);
                if (value < Tuning.NeedThreshold) anyUrgent = true;

                FurnitureInstance best = null;
                double bestScore = double.MaxValue;
                foreach (var f in World.Furniture)
                {
                    if (f.Board != cat.Cell.Board || f.Def.Satisfies != need || !f.HasFreeSlot) continue;
                    if (value >= EffectiveThreshold(cat, f.Def)) continue;
                    int d = DistanceFrom(Maps.Get(f.Id), cat.Cell);
                    if (d == DistanceMap.Unreachable) continue;
                    double score = (d + 1) / cat.Personality.Love(f.Def) + (ExtraCost?.Invoke(cat, f) ?? 0) + _rng.NextDouble() * 0.01;
                    if (score < bestScore) { best = f; bestScore = score; }
                }
                if (best == null) continue;

                cat.SlotIndex = best.ReserveSlot(cat.Id);
                cat.TargetFurnitureId = best.Id;
                cat.TargetNeed = need;
                cat.State = CatState.MovingToTarget;
                cat.Act = CatAct.None;
                cat.Zoomies = false;
                return true;
            }

            // 급한데 쓸 수 있는 가구가 없음 (전부 정원 초과 등). 잠깐 쉬었다 다시 찾는다.
            if (anyUrgent) cat.RetargetCooldown = Tuning.RetargetCooldownSeconds;
            return false;
        }

        private int DistanceFrom(DistanceMap map, Cell c)
        {
            if (map == null) return DistanceMap.Unreachable;
            int d = map.Get(c);
            if (d != DistanceMap.Unreachable) return d;
            // 다른 가구의 진입 칸처럼 거리맵에 없는 칸에 서 있으면 이웃에서 한 칸 더한 값
            int best = DistanceMap.Unreachable;
            foreach (var n in World.Neighbors(c, throughDoor: false))
            {
                int nd = map.Get(n);
                if (nd != DistanceMap.Unreachable && nd + 1 < best) best = nd + 1;
            }
            return best;
        }

        private void UpdateMoving(Cat cat)
        {
            var f = World.GetFurniture(cat.TargetFurnitureId);
            if (f == null) { AbortTarget(cat); return; }

            var map = Maps.Get(f.Id);
            if (map.Get(cat.Cell) == 0)
            {
                cat.State = CatState.Entering;
                cat.StateTimer = Tuning.EnterSeconds;
                return;
            }
            if (map.TryStep(World, cat.Cell, out var next, _rng.NextDouble(), throughDoor: false)) StartHop(cat, next);
            else AbortTarget(cat);
        }

        private void AbortTarget(Cat cat)
        {
            ReleaseTarget(cat);
            cat.LoungeVisit = false;
            EndLounge(cat);
            cat.RetargetCooldown = Tuning.RetargetCooldownSeconds;
            StartWander(cat);
        }

        private void BeginExit(Cat cat)
        {
            cat.State = CatState.Exiting;
            cat.StateTimer = Tuning.ExitSeconds;
        }

        private void FinishExit(Cat cat)
        {
            var f = World.GetFurniture(cat.TargetFurnitureId);
            // 멍때리러 갔던 건 가구를 '사용'한 게 아니라서 트레이·하트가 없다
            bool completed = f != null && !cat.LoungeVisit;
            // 잠자리에서 자고 나왔거나 가구 위에 누워 있다 내려오면 기지개
            bool slept = completed && cat.TargetNeed == NeedType.Energy
                || cat.LoungeVisit && f != null && (cat.IsLyingDown || f.Def.Interaction == InteractionType.Enter);
            double stretchChance = completed ? Tuning.WakeStretchChance : Tuning.LieStretchChance;
            cat.LoungeVisit = false;
            cat.Lounge = LoungeKind.None;
            ReleaseTarget(cat);
            if (completed) InteractionCompleted?.Invoke(cat, f);

            // 진입 칸은 지나가는 길이 아니므로 옆 칸으로 한 걸음 나온다
            if (!World.IsTransit(cat.Cell))
            {
                var outs = new List<Cell>();
                foreach (var n in World.Neighbors(cat.Cell, throughDoor: false))
                    if (CanWanderInto(cat, n)) outs.Add(n);
                if (outs.Count > 0) StartHop(cat, outs[_rng.Next(outs.Count)]);
            }
            EnterIdle(cat, 0.5, 1.5);
            if (slept && _rng.NextDouble() < stretchChance) StartAct(cat, CatAct.Stretch);
        }

        // ───────────────────────── 바깥(놀아주기 등)에서 쓰는 것 ─────────────────────────

        /// <summary>가구 예약·멍때리기·몸짓을 모두 내려놓는다.</summary>
        public void ClearPlans(Cat cat)
        {
            if (cat.HasTarget) ReleaseTarget(cat);
            cat.LoungeVisit = false;
            EndLounge(cat);
            cat.Act = CatAct.None;
            cat.Zoomies = false;
        }

        /// <summary>잠깐 멍 (다음 행동은 평소처럼 고른다).</summary>
        public void SetIdle(Cat cat) => EnterIdle(cat, 0.5, 1.5);

        /// <summary>지금 칸에서 누워 쉰다 (지쳤을 때).</summary>
        public void Rest(Cat cat, LoungeKind kind, double seconds)
        {
            ClearPlans(cat);
            cat.Lounge = kind;
            cat.LoungeTimer = seconds;
            cat.State = CatState.Lounging;
        }

        /// <summary>이 칸으로 걸어갈 수 있는지 (같은 보드, 지나갈 수 있는 칸, 문 제외).</summary>
        public bool CanWalk(Cat cat, Cell n) => CanWanderInto(cat, n);

        /// <summary>짧은 몸짓을 시작한다 (기지개·하품·귀 긁기). 끝나면 잠깐 멍.</summary>
        public void StartAct(Cat cat, CatAct act)
        {
            cat.State = CatState.Acting;
            cat.Act = act;
            cat.Zoomies = false;
            cat.StateTimer = act == CatAct.Stretch ? Tuning.StretchSeconds : act == CatAct.Yawn ? Tuning.YawnSeconds : Tuning.EarScratchSeconds;
        }

        private void ReleaseTarget(Cat cat)
        {
            var f = World.GetFurniture(cat.TargetFurnitureId);
            f?.ReleaseSlot(cat.SlotIndex, cat.Id);
            cat.TargetFurnitureId = -1;
            cat.SlotIndex = -1;
            cat.TargetNeed = NeedType.None;
        }

        // ───────────────────────── 멍때리기 ─────────────────────────

        private bool AnyUrgent(Cat cat)
        {
            foreach (var n in CatNeeds.All) if (cat.Needs.Get(n) < Tuning.NeedThreshold) return true;
            return false;
        }

        /// <summary>
        /// 앉아서 또는 누워서 멍. 가끔은 캣타워·숨숨집·박스·방석에 올라가서 (좋아하는 가구일수록 잘 고름),
        /// 아니면 지금 서 있는 빈칸에서. 성격의 IdleScale 만큼 오래 멍때린다.
        /// </summary>
        private void StartLounge(Cat cat)
        {
            cat.Lounge = _rng.NextDouble() < Tuning.LieChance ? LoungeKind.Lie : LoungeKind.Sit;
            cat.LoungeTimer = Range(Tuning.LoungeMinSeconds, Tuning.LoungeMaxSeconds) * Math.Max(0.5, cat.Personality.IdleScale);

            // 해가 들어오면 가끔 창문 빛 속으로 간다 (대개 누워서, 더 오래)
            if (IsSunny != null && _rng.NextDouble() < Math.Min(0.95, Tuning.SunbathChance * cat.Personality.SunLove)
                && TryGoToSun(cat, Tuning.SunSearchRadius))
            {
                cat.Sunbathing = true;
                double pick = _rng.NextDouble();
                cat.Lounge = pick < Tuning.SprawlChance ? LoungeKind.Sprawl : pick < 0.85 ? LoungeKind.Lie : LoungeKind.Sit;
                cat.LoungeTimer = Range(Tuning.SunLoungeMinSeconds, Tuning.SunLoungeMaxSeconds) * Math.Max(0.6, cat.Personality.IdleScale);
                return;
            }

            cat.Act = CatAct.None;
            cat.Zoomies = false;
            if (_rng.NextDouble() < Tuning.LoungeFurnitureChance && TryReserveLoungeSpot(cat))
            {
                cat.LoungeVisit = true;
                cat.State = CatState.MovingToTarget;
                return;
            }
            var t = World.GetTile(cat.Cell);
            if (t == null || t.Kind == TileKind.Door || !World.IsTransit(cat.Cell)) { cat.Lounge = LoungeKind.None; StartWander(cat); return; }
            cat.State = CatState.Lounging;
        }

        private void EndLounge(Cat cat)
        {
            cat.Lounge = LoungeKind.None;
            cat.Sunbathing = false;
            cat.SpotPath = null;
        }

        /// <summary>
        /// 가까운 햇빛 칸(다른 고양이가 쬐고 있지 않은 곳)을 찾아 간다. 지금 칸이 빛이면 그대로 누른다.
        /// 길은 같은 보드의 지나갈 수 있는 칸으로만 (문 제외). 찾으면 true.
        /// </summary>
        private bool TryGoToSun(Cat cat, int maxDist)
        {
            if (IsSunny == null) return false;
            bool Free(Cell c)
            {
                foreach (var o in _cats)
                {
                    if (o == cat || !o.Sunbathing) continue;
                    if (o.Cell == c) return false;
                    // 다른 고양이가 가는 중인 자리도 남의 자리
                    if (o.State == CatState.ToSpot && o.SpotPath != null && o.SpotPath.Count > 0 && o.SpotPath[o.SpotPath.Count - 1] == c) return false;
                }
                return true;
            }
            if (IsSunny(cat.Cell) && Free(cat.Cell) && World.IsTransit(cat.Cell)) { cat.SpotPath = null; cat.State = CatState.Lounging; return true; }

            var prev = new Dictionary<Cell, Cell>();
            var depth = new Dictionary<Cell, int>();
            var queue = new Queue<Cell>();
            prev[cat.Cell] = cat.Cell;
            depth[cat.Cell] = 0;
            queue.Enqueue(cat.Cell);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (depth[cur] >= maxDist) continue;
                foreach (var n in World.Neighbors(cur, throughDoor: false))
                {
                    if (prev.ContainsKey(n) || !CanWanderInto(cat, n)) continue;
                    prev[n] = cur;
                    depth[n] = depth[cur] + 1;
                    if (IsSunny(n) && Free(n))
                    {
                        var path = new List<Cell>();
                        for (var c = n; c != cat.Cell; c = prev[c]) path.Add(c);
                        path.Reverse();
                        cat.SpotPath = path;
                        cat.State = CatState.ToSpot;
                        return true;
                    }
                    queue.Enqueue(n);
                }
            }
            return false;
        }

        /// <summary>올라가거나 들어가서 멍때릴 가구 (캣타워·숨숨집·박스·방석·침대). 가깝고 좋아하는 곳.</summary>
        private bool TryReserveLoungeSpot(Cat cat)
        {
            FurnitureInstance best = null;
            double bestScore = double.MaxValue;
            foreach (var f in World.Furniture)
            {
                if (f.Board != cat.Cell.Board || !f.HasFreeSlot) continue;
                var it = f.Def.Interaction;
                if (it != InteractionType.Climb && it != InteractionType.Enter) continue;
                int d = DistanceFrom(Maps.Get(f.Id), cat.Cell);
                if (d == DistanceMap.Unreachable) continue;
                double score = (d + 1) / cat.Personality.Love(f.Def) + _rng.NextDouble() * 2;
                if (score < bestScore) { best = f; bestScore = score; }
            }
            if (best == null) return false;
            cat.SlotIndex = best.ReserveSlot(cat.Id);
            cat.TargetFurnitureId = best.Id;
            cat.TargetNeed = NeedType.None;
            return true;
        }

        // ───────────────────────── 배회 ─────────────────────────

        private void StartWander(Cat cat, bool mayZoom = false)
        {
            cat.State = CatState.Wandering;
            cat.Act = CatAct.None;
            cat.Zoomies = mayZoom && WantsZoomies(cat);
            if (cat.Zoomies) { cat.WanderStepsLeft = _rng.Next(Tuning.ZoomiesMinSteps, Tuning.ZoomiesMaxSteps + 1); return; }
            int steps = _rng.Next(Tuning.WanderMinSteps, Tuning.WanderMaxSteps + 1);
            cat.WanderStepsLeft = Math.Max(1, (int)Math.Round(steps * cat.Personality.WanderScale));
        }

        /// <summary>우다다 할 기분인지: 기운이 넘치고, 밤이거나 에너자이저일 때 확률로.</summary>
        private bool WantsZoomies(Cat cat)
        {
            if (cat.Affiliation != Affiliation.Resident || cat.Needs.Energy < Tuning.ZoomiesMinEnergy || AnyUrgent(cat)) return false;
            double p = 0;
            if (IsNight != null && IsNight()) p += Tuning.ZoomiesNightChance;
            if (cat.Personality.Id == "energizer") p += Tuning.ZoomiesEnergizerChance;
            return p > 0 && _rng.NextDouble() < p;
        }

        private void EnterIdle(Cat cat) => EnterIdle(cat, Tuning.IdleMinSeconds, Tuning.IdleMaxSeconds);

        private void EnterIdle(Cat cat, double min, double max)
        {
            cat.State = CatState.Idle;
            cat.Act = CatAct.None;
            cat.Zoomies = false;
            cat.StateTimer = Range(min, max) * cat.Personality.IdleScale;
        }

        /// <summary>
        /// 직진 선호 6방향 배회. 갈 수 있는 방향만 남기고 가중치로 고른다.
        /// 되돌아가기는 다른 길이 하나도 없을 때만.
        /// </summary>
        private bool WanderStep(Cat cat)
        {
            int[] rel = { 0, 1, -1, 2, -2 };
            double[] w = { Tuning.StraightWeight, Tuning.Turn60Weight, Tuning.Turn60Weight, Tuning.Turn120Weight, Tuning.Turn120Weight };

            var dirs = new List<int>(5);
            var weights = new List<double>(5);
            double total = 0;
            for (int i = 0; i < rel.Length; i++)
            {
                int dir = ((cat.Heading + rel[i]) % 6 + 6) % 6;
                var n = new Cell(cat.Cell.Board, cat.Cell.Coord.Neighbor(dir));
                if (!CanWanderInto(cat, n)) continue;
                dirs.Add(dir);
                weights.Add(w[i]);
                total += w[i];
            }

            if (dirs.Count == 0)
            {
                int back = (cat.Heading + 3) % 6;
                var n = new Cell(cat.Cell.Board, cat.Cell.Coord.Neighbor(back));
                if (!CanWanderInto(cat, n)) return false;
                StartHop(cat, n);
                cat.WanderStepsLeft--;
                return true;
            }

            double roll = _rng.NextDouble() * total;
            int pick = dirs.Count - 1;
            for (int i = 0; i < dirs.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0) { pick = i; break; }
            }
            StartHop(cat, new Cell(cat.Cell.Board, cat.Cell.Coord.Neighbor(dirs[pick])));
            cat.WanderStepsLeft--;
            return true;
        }

        /// <summary>배회로 들어갈 수 있는 칸: 같은 보드, 지나갈 수 있는 칸, 문은 제외 (실내 고양이는 집 안에서만).</summary>
        private bool CanWanderInto(Cat cat, Cell n)
        {
            if (n.Board != cat.Cell.Board) return false;
            var t = World.GetTile(n);
            if (t == null || t.Kind == TileKind.Door) return false;
            return World.IsTransit(n);
        }

        private void StartHop(Cat cat, Cell next)
        {
            if (next.Board == cat.Cell.Board)
            {
                var diff = next.Coord - cat.Cell.Coord;
                for (int i = 0; i < 6; i++) if (HexCoord.Directions[i] == diff) { cat.Heading = i; break; }
            }
            cat.PrevCell = cat.Cell;
            cat.Cell = next;
            cat.HopProgress = 0;
            cat.HopDuration = Tuning.HopSeconds / Math.Max(0.1, cat.Personality.MoveSpeed * cat.SpeedJitter);
            if (cat.Zoomies && cat.State == CatState.Wandering)
            {
                // 우다다: 훨씬 빠르게, 신나지만 기운이 든다
                cat.HopDuration *= Tuning.ZoomiesHopScale;
                cat.Needs.Add(NeedType.Fun, Tuning.ZoomiesFunPerStep);
                cat.Needs.Add(NeedType.Energy, -Tuning.ZoomiesEnergyPerStep);
            }
            CatHopped?.Invoke(cat);
        }

        /// <summary>기분은 아무 일 없으면 천천히 평소값(70)으로 돌아간다.</summary>
        private void DriftMood(Cat cat, double dt)
        {
            const double baseline = 70, rate = 0.05;
            if (cat.Mood > baseline) cat.Mood = Math.Max(baseline, cat.Mood - rate * dt);
            else if (cat.Mood < baseline) cat.Mood = Math.Min(baseline, cat.Mood + rate * dt);
        }

        private double Range(double min, double max) => min + _rng.NextDouble() * (max - min);
    }
}

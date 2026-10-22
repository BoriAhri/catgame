using System;
using System.Collections.Generic;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.World;

namespace CatHealingHouse.Cats
{
    /// <summary>놀아주기 장난감. 레이저는 처음부터, 낚싯대는 상점에서 산다.</summary>
    public enum ToyKind { Laser, Wand }

    /// <summary>놀아주기 수치. 나중에 플레이해 보며 조정한다.</summary>
    [Serializable]
    public sealed class PlayTuning
    {
        /// <summary>다 같이 놀기: 장난감에서 이 칸 수 안에 있는 고양이만 달려든다.</summary>
        public int JoinRadius = 3;
        /// <summary>장난감이 이만큼 멀어진 채 LoseSeconds 가 지나면 흥미를 잃고 그만둔다 (다 같이 놀기).</summary>
        public int LoseRadius = 5;
        public double LoseSeconds = 2.5;
        /// <summary>장난감이 사라진(레이저를 뗀) 채 이만큼 지나면 그만둔다 (다 같이 놀기).</summary>
        public double GoneSeconds = 4;

        /// <summary>쫓아갈 때 한 칸 걸음 시간 배율 (작을수록 빠름).</summary>
        public double LaserHopScale = 0.42, WandHopScale = 0.5;

        /// <summary>덮칠 수 있는 장난감 속도 (초당 칸). 이보다 빨리 움직이면 웅크리고 노려본다.</summary>
        public double CatchSpeed = 1.6;
        /// <summary>장난감이 마지막으로 움직인 뒤 이 시간 안에만 덮친다 (가만히 둔 낚싯대는 금방 시시해한다).</summary>
        public double FreshSeconds = 3;
        public double PounceSeconds = 0.6;
        public double CatchCooldown = 1.4;

        /// <summary>한 번 잡을 때.</summary>
        public double LaserExp = 1.5, WandExp = 3;
        public double FunPerCatch = 10, MoodPerCatch = 2;
        public double EnergyPerCatch = 6, EnergyPerHop = 0.35;

        /// <summary>기력이 이 아래로 내려가면 지쳐서 드러눕고, RestSeconds 동안은 다시 안 논다.</summary>
        public double TiredEnergy = 25;
        public double RestSeconds = 25;
        /// <summary>배고픔·화장실이 이 아래면 놀다가도 그만둔다.</summary>
        public double QuitNeed = 15;

        /// <summary>하루에 이만큼 경험치까지는 다 받고, 그다음부터는 LateShare 만큼만.</summary>
        public double DailyFullExp = 40, LateShare = 0.3;

        public double HopScale(ToyKind k) => k == ToyKind.Wand ? WandHopScale : LaserHopScale;
        public double Exp(ToyKind k) => k == ToyKind.Wand ? WandExp : LaserExp;
    }

    /// <summary>
    /// 놀아주기 (사냥 놀이). 손가락으로 움직이는 장난감(레이저 점·낚싯대 깃털)을 고양이가 쫓아가서
    /// 천천히 멈춘 장난감을 덮치면 '잡기'. 잡을 때마다 친밀도 경험치·즐거움·기분이 오르고 기력이 줄어든다.
    /// 기력이 바닥나면 지쳐서 드러눕는다.
    ///  - 한 마리와 놀기 (프로필 버튼): 그 고양이만, 방 어디서든 쫓아온다.
    ///  - 다 같이 놀기 (아래 메뉴): 장난감 근처(JoinRadius 칸)에 있는 고양이만 달려든다.
    /// 걸음 진행은 시뮬레이션이 하고, 이 두뇌는 걸음이 끝날 때마다 다음 행동을 고른다 (CatSimulation.PlayBrain).
    /// </summary>
    public sealed class PlaySystem
    {
        public readonly CatSimulation Sim;
        public readonly Intimacy Intimacy;
        public readonly PlayTuning Tuning;

        public bool Active { get; private set; }
        public ToyKind Toy { get; private set; }
        /// <summary>한 마리와 놀기면 그 고양이 (다 같이면 null).</summary>
        public Cat Solo { get; private set; }

        /// <summary>장난감이 있는 칸 (레이저를 떼면 null).</summary>
        public Cell? ToyCell { get; private set; }
        public double ToySpeed { get; private set; }
        private double _lastMoveAt = double.NegativeInfinity;

        /// <summary>날짜 번호 (하루 경험치 감소용). 화면 쪽이 정한다.</summary>
        public Func<int> Today = () => 0;

        private readonly List<Cat> _players = new List<Cat>();
        private readonly Dictionary<Cat, double> _farSince = new Dictionary<Cat, double>();
        private double _goneSince = double.NegativeInfinity;
        private readonly Random _rng;

        /// <summary>잡았다 (고양이, 받은 경험치).</summary>
        public event Action<Cat, double> Caught;
        /// <summary>지쳐서 그만둠.</summary>
        public event Action<Cat> Tired;
        /// <summary>달려들기 시작.</summary>
        public event Action<Cat> Joined;

        public IReadOnlyList<Cat> Players => _players;

        public PlaySystem(CatSimulation sim, Intimacy intimacy, PlayTuning tuning = null, int seed = 777)
        {
            Sim = sim;
            Intimacy = intimacy;
            Tuning = tuning ?? new PlayTuning();
            _rng = new Random(seed);
            sim.PlayBrain = Brain;
            sim.CatRemoved += c => Leave(c, false);
        }

        public bool IsPlaying(Cat c) => _players.Contains(c);

        /// <summary>놀이 시작. solo 가 있으면 그 고양이하고만.</summary>
        public void Start(ToyKind toy, Cat solo = null)
        {
            Stop();
            Active = true;
            Toy = toy;
            Solo = solo;
            ToyCell = null;
            ToySpeed = 0;
            _goneSince = Sim.SimTime;
        }

        public void SetToyKind(ToyKind toy) => Toy = toy;

        /// <summary>놀이를 끝낸다. 놀던 고양이는 잠깐 멍.</summary>
        public void Stop()
        {
            foreach (var c in _players.ToArray()) Leave(c, true);
            Active = false;
            Solo = null;
            ToyCell = null;
        }

        /// <summary>장난감 위치 (화면이 매 프레임). cell 이 null 이면 장난감이 사라짐. speed 는 초당 칸.</summary>
        public void SetToy(Cell? cell, double speed)
        {
            if (cell.HasValue && ToyCell.HasValue && cell.Value != ToyCell.Value) _lastMoveAt = Sim.SimTime;
            if (cell.HasValue && !ToyCell.HasValue) _lastMoveAt = Sim.SimTime;
            if (speed > 0.3) _lastMoveAt = Sim.SimTime;
            if (!cell.HasValue && ToyCell.HasValue) _goneSince = Sim.SimTime;
            ToyCell = cell;
            ToySpeed = speed;
        }

        /// <summary>오늘 이 고양이가 다음에 잡으면 받을 경험치.</summary>
        public double NextExp(Cat c)
        {
            double exp = Tuning.Exp(Toy);
            int day = Today();
            double got = c.PlayDay == day ? c.PlayExpToday : 0;
            return got >= Tuning.DailyFullExp ? exp * Tuning.LateShare : exp;
        }

        public bool IsTired(Cat c) => c.Needs.Energy < Tuning.TiredEnergy || c.PlayRestUntil > Sim.SimTime;

        // ───────────────────────── 두뇌 ─────────────────────────

        /// <summary>시뮬레이션이 집 고양이마다 (걸음 중이 아닐 때) 부른다. true 면 이번 틱은 놀이가 맡았다.</summary>
        private bool Brain(Cat cat, double dt)
        {
            if (cat.Affiliation != Affiliation.Resident) return false;
            bool playing = _players.Contains(cat);
            if (!Active) { if (playing) Leave(cat, true); return false; }

            if (!playing)
            {
                if (!WantsToJoin(cat)) return false;
                Join(cat);
            }

            if (cat.PlayPounce > 0)
            {
                cat.PlayPounce -= dt;
                return true;
            }
            if (cat.PlayCatchCooldown > 0) cat.PlayCatchCooldown -= dt;

            // 그만둘 때: 지침, 급한 볼일, 흥미 잃음
            if (cat.Needs.Energy < Tuning.TiredEnergy)
            {
                Leave(cat, false);
                cat.PlayRestUntil = Sim.SimTime + Tuning.RestSeconds;
                Sim.Rest(cat, LoungeKind.Sprawl, Tuning.RestSeconds);
                Tired?.Invoke(cat);
                return true;
            }
            if (cat.Needs.Hunger < Tuning.QuitNeed || cat.Needs.Bladder < Tuning.QuitNeed) { Leave(cat, true); return false; }

            if (!ToyCell.HasValue || ToyCell.Value.Board != cat.Cell.Board)
            {
                // 장난감이 사라짐: 웅크리고 두리번. 다 같이 놀기에선 한참 안 나타나면 그만둔다.
                if (Solo == null && Sim.SimTime - _goneSince > Tuning.GoneSeconds) { Leave(cat, true); return false; }
                return true;
            }

            var toy = ToyCell.Value;
            int d = HexCoord.Distance(cat.Cell.Coord, toy.Coord);
            if (Solo == null)
            {
                if (d > Tuning.LoseRadius)
                {
                    if (!_farSince.ContainsKey(cat)) _farSince[cat] = Sim.SimTime;
                    else if (Sim.SimTime - _farSince[cat] > Tuning.LoseSeconds) { Leave(cat, true); return false; }
                }
                else _farSince.Remove(cat);
            }

            // 장난감이 가구 위처럼 못 들어가는 칸이면 바로 옆 칸에서 덮친다
            bool reach = d == 0 || (d == 1 && !Sim.CanWalk(cat, toy));
            if (reach)
            {
                if (d == 1) FaceToward(cat, toy);
                // 천천히 멈춘(그리고 방금 움직였던) 장난감이면 덮친다, 빠르면 웅크리고 노려본다
                bool fresh = Sim.SimTime - _lastMoveAt <= Tuning.FreshSeconds;
                if (ToySpeed <= Tuning.CatchSpeed && fresh && cat.PlayCatchCooldown <= 0) Catch(cat);
                return true;
            }

            // 한 칸씩 장난감 쪽으로 (빨리)
            if (!StepToward(cat, toy)) { FaceToward(cat, toy); return true; }
            cat.Needs.Add(NeedType.Energy, -Tuning.EnergyPerHop);
            return true;
        }

        private bool WantsToJoin(Cat cat)
        {
            if (cat.IsInsideFurniture || cat.Held || IsTired(cat)) return false;
            if (cat.Needs.Hunger < Tuning.QuitNeed || cat.Needs.Bladder < Tuning.QuitNeed) return false;
            if (Solo != null) return cat == Solo;
            if (!ToyCell.HasValue || ToyCell.Value.Board != cat.Cell.Board) return false;
            return HexCoord.Distance(cat.Cell.Coord, ToyCell.Value.Coord) <= Tuning.JoinRadius;
        }

        private void Join(Cat cat)
        {
            Sim.ClearPlans(cat);
            cat.State = CatState.Playing;
            cat.PlayPounce = 0;
            cat.PlayCatchCooldown = 0.3;
            _players.Add(cat);
            _farSince.Remove(cat);
            Joined?.Invoke(cat);
        }

        private void Leave(Cat cat, bool idle)
        {
            if (!_players.Remove(cat)) return;
            _farSince.Remove(cat);
            cat.PlayPounce = 0;
            if (idle && cat.State == CatState.Playing) Sim.SetIdle(cat);
        }

        private void Catch(Cat cat)
        {
            cat.PlayPounce = Tuning.PounceSeconds;
            cat.PlayCatchCooldown = Tuning.CatchCooldown;
            int day = Today();
            if (cat.PlayDay != day) { cat.PlayDay = day; cat.PlayExpToday = 0; }
            double exp = Intimacy.IsMax(cat) ? 0 : NextExp(cat);
            cat.PlayExpToday += exp;
            cat.Needs.Add(NeedType.Fun, Tuning.FunPerCatch);
            cat.Needs.Add(NeedType.Energy, -Tuning.EnergyPerCatch);
            cat.AddMood(Tuning.MoodPerCatch);
            if (exp > 0) Intimacy.Gain(cat, exp);
            Caught?.Invoke(cat, exp);
        }

        /// <summary>장난감에 더 가까워지는 이웃 칸으로 한 걸음 (같은 거리면 아무거나).</summary>
        private bool StepToward(Cat cat, Cell toy)
        {
            int cur = HexCoord.Distance(cat.Cell.Coord, toy.Coord);
            Cell? best = null;
            int bestD = cur;
            int ties = 0;
            for (int dir = 0; dir < 6; dir++)
            {
                var n = new Cell(cat.Cell.Board, cat.Cell.Coord.Neighbor(dir));
                if (!Sim.CanWalk(cat, n)) continue;
                int d = HexCoord.Distance(n.Coord, toy.Coord);
                if (d < bestD) { bestD = d; best = n; ties = 1; }
                else if (d == bestD && best.HasValue && d < cur && _rng.Next(++ties) == 0) best = n;
            }
            if (!best.HasValue) return false;
            Sim.Hop(cat, best.Value, Tuning.HopScale(Toy));
            return true;
        }

        /// <summary>제자리에서 장난감 쪽을 본다.</summary>
        private static void FaceToward(Cat cat, Cell toy)
        {
            int best = cat.Heading, bestD = int.MaxValue;
            for (int dir = 0; dir < 6; dir++)
            {
                int d = HexCoord.Distance(cat.Cell.Coord.Neighbor(dir), toy.Coord);
                if (d < bestD) { bestD = d; best = dir; }
            }
            cat.Heading = best;
        }
    }
}

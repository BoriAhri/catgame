using System;
using CatHealingHouse.Economy;
using CatHealingHouse.Stray;

namespace CatHealingHouse.Cats
{
    /// <summary>확대해서 쓰다듬는 부위 (판 위 옆모습 고양이 기준).</summary>
    public enum PetZone { None, Head, Cheek, Chin, Chest, Belly, Paw, Tail, Nose, Back, Rump }

    /// <summary>그 부위를 만졌을 때의 기분.</summary>
    public enum PetFeel { Love, Like, Meh, Dislike }

    [Serializable]
    public sealed class PetTuning
    {
        /// <summary>좋아하는 곳을 화면 폭(정규화 1)만큼 문지르면 오르는 만족도. 1 이면 다 참.</summary>
        public double SatisfyPerUnit = 0.35;
        public double AnnoyPerUnit = 0.55;
        public double AnnoyDecayPerSec = 0.12;
        public double LikeScale = 0.6, MehScale = 0.12, MehAnnoy = 0.12;
        /// <summary>한 번 꽉 채우면 피로 +0.7. 피로만큼 효과가 줄고 10분이면 다 풀린다.</summary>
        public double FatiguePerSession = 0.7;
        public double FatigueRecoverPerSec = 1.0 / 600;
        public double MinGainScale = 0.15;
        /// <summary>능숙한 손길 레벨당 만족 속도 +20%.</summary>
        public double SkillGainPerLevel = 0.2;
        /// <summary>집 고양이: 만족도를 0→1 채우는 동안 오르는 기분 합계.</summary>
        public double MoodPerSession = 14;
        public double AnnoyMoodPenalty = 3;
        /// <summary>꽉 채우면 젤리 하트. 고양이마다 이 시간(시뮬레이션 초)에 한 번.</summary>
        public long HeartReward = 1;
        public double HeartCooldown = 1800;
        /// <summary>길냥이: 꽉 채우면 방문당 한 번 호감도 추가.</summary>
        public double StrayBonusAffection = 3;
        public double StrayAnnoyPenalty = 1;
        public double NoseBoopMood = 0.5;
        /// <summary>만족도가 이만큼 차면 스스로 발라당 (한 번에 한 번).</summary>
        public double RollAt = 0.8;
        /// <summary>배를 안 좋아하는 고양이가 발라당했을 때 배를 만지면 (배 함정): 짜증 +이만큼, 뒷발 팡팡 하고 다시 일어남.</summary>
        public double TrapAnnoy = 0.35;
    }

    /// <summary>성격별 좋아하는 곳·싫어하는 곳.</summary>
    public static class PetPrefs
    {
        public static PetFeel Base(PetZone z)
        {
            switch (z)
            {
                case PetZone.Head:
                case PetZone.Cheek: return PetFeel.Love;
                case PetZone.Chin:
                case PetZone.Back:
                case PetZone.Rump:
                case PetZone.Chest: return PetFeel.Like;
                case PetZone.Nose: return PetFeel.Meh;
                default: return PetFeel.Dislike; // 배, 앞발, 꼬리
            }
        }

        public static PetFeel ForPersonality(string id, PetZone z)
        {
            var f = Base(z);
            switch (id)
            {
                case "sleepyhead": if (z == PetZone.Belly) return PetFeel.Like; if (z == PetZone.Chin) return PetFeel.Love; break;
                case "tower_king": if (z == PetZone.Chest) return PetFeel.Meh; break;
                case "energizer": if (z == PetZone.Paw || z == PetZone.Tail) return PetFeel.Meh; if (z == PetZone.Cheek) return PetFeel.Like; if (z == PetZone.Back || z == PetZone.Rump) return PetFeel.Love; break;
                case "foodie": if (z == PetZone.Chin) return PetFeel.Love; if (z == PetZone.Belly) return PetFeel.Meh; break;
                case "shy": if (z == PetZone.Head) return PetFeel.Like; if (z == PetZone.Chest || z == PetZone.Rump) return PetFeel.Meh; break;
                case "curious": if (z == PetZone.Nose || z == PetZone.Paw) return PetFeel.Like; if (z == PetZone.Rump) return PetFeel.Love; break;
            }
            return f;
        }

        /// <summary>길냥이 성격: 싸가지없음은 한 단계 까다롭게, 개냥이는 한 단계 너그럽게.</summary>
        public static PetFeel ForStray(Temperament t, PetZone z)
        {
            var f = Base(z);
            if (t == Temperament.Sassy) return f == PetFeel.Love ? PetFeel.Like : f == PetFeel.Like ? PetFeel.Meh : PetFeel.Dislike;
            if (t == Temperament.Cuddly)
            {
                if (z == PetZone.Belly) return PetFeel.Like; // 개냥이는 배도 내준다
                return f == PetFeel.Dislike ? (z == PetZone.Tail ? PetFeel.Dislike : PetFeel.Meh) : f == PetFeel.Meh ? PetFeel.Like : PetFeel.Love;
            }
            return f;
        }
    }

    public enum PetEnd { None, Satisfied, Annoyed, Stopped }

    /// <summary>한 번 가까이 보기 동안의 쓰다듬기 기록.</summary>
    public sealed class PetSession
    {
        public readonly Cat Cat;
        public readonly bool IsStray;
        internal readonly Func<PetZone, PetFeel> FeelOf;
        internal readonly double GainScale;
        public double Satisfaction;   // 0~1
        public double Annoyance;      // 0~1
        public double MoodGiven;
        public PetEnd End;
        public PetZone LastZone;
        public PetFeel LastFeel = PetFeel.Meh;
        /// <summary>발라당 누워서 배를 보여주는 중.</summary>
        public bool BellyUp;
        /// <summary>이번에 이미 발라당했다 (한 번만).</summary>
        public bool Rolled;
        /// <summary>발라당했을 때 배를 만져도 좋아하는 고양이 (아니면 배 함정).</summary>
        public bool BellyLover;
        /// <summary>배 함정에 걸려 뒷발로 차인 횟수 (화면 연출용).</summary>
        public int Kicks;
        // 보상 결과 (끝날 때 채워짐)
        public long HeartsGiven;
        public double AffectionGiven;
        public bool HeartOnCooldown;

        internal PetSession(Cat cat, bool stray, Func<PetZone, PetFeel> feel, double gainScale)
        {
            Cat = cat; IsStray = stray; FeelOf = feel; GainScale = gainScale;
        }

        public bool Done => End != PetEnd.None;
        public PetFeel Feel(PetZone z) => FeelOf(z);
    }

    /// <summary>
    /// 가까이 보기 쓰다듬기 (8단계). 좋아하는 곳을 문지르면 만족도가 차고, 싫은 곳을 계속 만지면 짜증이 차서 가 버린다.
    /// 만족도를 다 채우면: 집 고양이는 기분(채우는 동안 조금씩) + 젤리 하트(고양이마다 30분에 한 번),
    /// 길냥이는 호감도 보너스(방문당 한 번) + 젤리 하트. 같은 고양이를 계속 쓰다듬으면 피로 때문에 효과가 준다.
    /// </summary>
    public sealed class Petting
    {
        public readonly CatSimulation Sim;
        public readonly Wallet Wallet;
        public readonly StraySystem Strays;
        public readonly PetTuning Tuning;
        /// <summary>능숙한 손길 레벨 (없으면 0).</summary>
        public Func<int> SkillLevel = () => 0;

        public Petting(CatSimulation sim, Wallet wallet, StraySystem strays, PetTuning tuning = null)
        {
            Sim = sim; Wallet = wallet; Strays = strays; Tuning = tuning ?? new PetTuning();
        }

        private double Now => Sim.SimTime;

        public double FatigueOf(Cat c) => Math.Max(0, c.PetFatigue - (Now - c.PetFatigueAt) * Tuning.FatigueRecoverPerSec);

        public double GainScaleOf(Cat c)
        {
            double fat = Math.Min(1, FatigueOf(c));
            return (1 - fat * (1 - Tuning.MinGainScale)) * (1 + SkillLevel() * Tuning.SkillGainPerLevel);
        }

        /// <summary>젤리 하트를 다시 받을 수 있을 때까지 남은 초 (0 이면 지금 가능).</summary>
        public double HeartWait(Cat c) => Math.Max(0, c.PetHeartAt - Now);

        public PetSession Begin(Cat cat)
        {
            if (cat == null) return null;
            bool stray = cat.Affiliation == Affiliation.Stray;
            Func<PetZone, PetFeel> feel;
            if (stray)
            {
                var p = Strays != null ? Strays.ProfileOf(cat) : null;
                var t = p != null ? p.Temperament : Temperament.Normal;
                feel = z => PetPrefs.ForStray(t, z);
            }
            else
            {
                string id = cat.Personality != null ? cat.Personality.Id : "normal";
                feel = z => PetPrefs.ForPersonality(id, z);
            }
            cat.Held = true;
            var s = new PetSession(cat, stray, feel, GainScaleOf(cat));
            var bf = feel(PetZone.Belly);
            s.BellyLover = bf == PetFeel.Love || bf == PetFeel.Like;
            return s;
        }

        /// <summary>length 는 화면 폭을 1 로 한 문지른 길이.</summary>
        public PetFeel Stroke(PetSession s, PetZone zone, double length)
        {
            if (s == null || s.Done || zone == PetZone.None || length <= 0) return PetFeel.Meh;
            var f = s.FeelOf(zone);
            // 발라당 중에 배: 좋아하는 고양이는 최고, 아니면 배 함정 (뒷발 팡팡 하고 일어남)
            if (zone == PetZone.Belly && s.BellyUp)
            {
                if (s.BellyLover) f = PetFeel.Love;
                else
                {
                    s.LastZone = zone;
                    s.LastFeel = PetFeel.Dislike;
                    s.BellyUp = false;
                    s.Kicks++;
                    s.Annoyance = Math.Min(1, s.Annoyance + Tuning.TrapAnnoy);
                    if (s.Annoyance >= 1) Finish(s, PetEnd.Annoyed);
                    return PetFeel.Dislike;
                }
            }
            s.LastZone = zone;
            s.LastFeel = f;
            double scale = f == PetFeel.Love ? 1 : f == PetFeel.Like ? Tuning.LikeScale : f == PetFeel.Meh ? Tuning.MehScale : 0;
            double annoy = f == PetFeel.Dislike ? 1 : f == PetFeel.Meh ? Tuning.MehAnnoy : 0;

            double gain = Math.Min(1 - s.Satisfaction, length * Tuning.SatisfyPerUnit * scale * s.GainScale);
            if (gain > 0)
            {
                s.Satisfaction += gain;
                // 좋은 손길은 짜증을 조금 풀어준다
                s.Annoyance = Math.Max(0, s.Annoyance - gain * 0.5);
                if (!s.IsStray)
                {
                    double mood = gain * Tuning.MoodPerSession;
                    s.Cat.AddMood(mood);
                    s.MoodGiven += mood;
                }
                double fat = FatigueOf(s.Cat) + gain * Tuning.FatiguePerSession;
                s.Cat.PetFatigue = fat;
                s.Cat.PetFatigueAt = Now;
            }
            if (annoy > 0) s.Annoyance = Math.Min(1, s.Annoyance + length * Tuning.AnnoyPerUnit * annoy);
            // 만족도가 반쯤 차면 스스로 발라당
            if (!s.Rolled && s.Satisfaction >= Tuning.RollAt && s.Satisfaction < 1 - 1e-9) { s.Rolled = true; s.BellyUp = true; }

            if (s.Annoyance >= 1) Finish(s, PetEnd.Annoyed);
            else if (s.Satisfaction >= 1 - 1e-9) Finish(s, PetEnd.Satisfied);
            return f;
        }

        /// <summary>코를 콕.</summary>
        public void Boop(PetSession s)
        {
            if (s == null || s.Done) return;
            if (!s.IsStray) { s.Cat.AddMood(Tuning.NoseBoopMood); s.MoodGiven += Tuning.NoseBoopMood; }
        }

        public void Tick(PetSession s, double dt)
        {
            if (s == null || s.Done) return;
            s.Annoyance = Math.Max(0, s.Annoyance - dt * Tuning.AnnoyDecayPerSec);
        }

        /// <summary>집사가 그만둘 때 (나가기 버튼·손가락 오므리기).</summary>
        public void Stop(PetSession s)
        {
            if (s == null || s.Done) return;
            Finish(s, PetEnd.Stopped);
        }

        private void Finish(PetSession s, PetEnd end)
        {
            s.End = end;
            s.BellyUp = false;
            var cat = s.Cat;
            cat.Held = false;
            if (end == PetEnd.Satisfied)
            {
                if (Now >= cat.PetHeartAt)
                {
                    Wallet.Add(ItemKind.Heart, Tuning.HeartReward);
                    s.HeartsGiven = Tuning.HeartReward;
                    cat.PetHeartAt = Now + Tuning.HeartCooldown;
                }
                else s.HeartOnCooldown = true;
                if (s.IsStray && Strays != null) s.AffectionGiven = Strays.PetBonus(cat, Tuning.StrayBonusAffection);
            }
            else if (end == PetEnd.Annoyed)
            {
                if (s.IsStray && Strays != null) s.AffectionGiven = -Strays.PetAnnoyed(cat, Tuning.StrayAnnoyPenalty);
                else { cat.AddMood(-Tuning.AnnoyMoodPenalty); s.MoodGiven -= Tuning.AnnoyMoodPenalty; }
            }
        }
    }
}

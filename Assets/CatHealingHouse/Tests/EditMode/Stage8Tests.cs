using System;
using CatHealingHouse.Cats;
using CatHealingHouse.Furniture;
using CatHealingHouse.Hex;
using CatHealingHouse.World;
using NUnit.Framework;

namespace CatHealingHouse.Tests
{
    public class Stage8Tests
    {
        private static Cat NewCat() => new Cat(1, "나비", new Cell(BoardId.Indoor, new HexCoord(0, 0)));

        [Test]
        public void HungryCatSaysHungry()
        {
            var c = NewCat();
            c.Needs.Hunger = 10;
            for (int i = 0; i < 20; i++)
            {
                var s = CatThoughts.Pick(c, null, new Random(i));
                Assert.That(s == "배고파..." || s == "밥 줘냥", s);
            }
        }

        [Test]
        public void LowestNeedWins()
        {
            var c = NewCat();
            c.Needs.Hunger = 25;
            c.Needs.Energy = 5;
            var s = CatThoughts.Pick(c, null, new Random(1));
            Assert.That(s == "졸려..." || s == "하암~", s);
        }

        [Test]
        public void HappyCatSaysSomethingNice()
        {
            var c = NewCat();
            c.Mood = 95;
            var s = CatThoughts.Pick(c, null, new Random(3));
            Assert.That(new[] { "여기 좋아~", "행복해냥", "집사 최고!" }, Contains.Item(s));
        }

        [Test]
        public void StrayStatesHaveLines()
        {
            var c = NewCat();
            c.Affiliation = Affiliation.Stray;
            c.StrayState = StrayState.Fleeing;
            Assert.AreEqual("무서워!", CatThoughts.Pick(c, null, new Random(0)));
            c.StrayState = StrayState.Leaving;
            Assert.IsNull(CatThoughts.Pick(c, null, new Random(0)));
        }

        [Test]
        public void NeverEmptyForResidents()
        {
            var c = NewCat();
            var rng = new Random(7);
            foreach (CatState st in Enum.GetValues(typeof(CatState)))
            {
                c.State = st;
                for (int m = 0; m <= 100; m += 25)
                {
                    c.Mood = m;
                    Assert.IsFalse(string.IsNullOrEmpty(CatThoughts.Pick(c, null, rng)), st + " " + m);
                }
            }
            Assert.IsFalse(string.IsNullOrEmpty(CatThoughts.Noticed(c, rng)));
        }
    }
}

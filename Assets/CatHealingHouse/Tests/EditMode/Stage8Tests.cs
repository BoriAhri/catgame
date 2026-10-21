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
        public void NoticedAlwaysSaysSomething()
        {
            var c = NewCat();
            var rng = new Random(7);
            foreach (CatState st in Enum.GetValues(typeof(CatState)))
            {
                c.State = st;
                for (int m = 0; m <= 100; m += 25) { c.Mood = m; Assert.IsFalse(string.IsNullOrEmpty(CatThoughts.Noticed(c, rng)), st + " " + m); }
            }
        }

        [Test]
        public void NoticedInLitterSaysDontLook()
        {
            var c = NewCat();
            c.State = CatState.Using;
            c.TargetNeed = NeedType.Bladder;
            Assert.AreEqual("보지 마!", CatThoughts.Noticed(c, new Random(0)));
        }
    }
}

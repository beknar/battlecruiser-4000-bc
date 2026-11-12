using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CarrierGame.Tests
{
    /// <summary>The industry's data (Resources/Data/industry.json): every recipe made of things
    /// that exist, every part reachable from mined minerals, every site doing something.</summary>
    public class IndustryDataTests
    {
        static IndustryTable T => IndustryTable.Get();

        [Test]
        public void EveryItemIsNamedOnceAndHasItsNumbers()
        {
            var ids = T.refined.Select(r => r.id).Concat(T.parts.Select(p => p.id)).ToArray();
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length), "every id once");
            foreach (var id in ids) Assert.That(IndustryTable.AsMineral(id, out _), Is.False, $"{id} isn't also a mineral");
            foreach (var r in T.refined)
            {
                Assert.That(r.name, Is.Not.Empty, r.id);
                Assert.That(r.form, Is.Not.Empty, r.id);
                Assert.That(r.makes, Is.GreaterThan(0f), r.id);
                Assert.That(r.seconds, Is.GreaterThan(0f), r.id);
                Assert.That(r.value, Is.GreaterThan(0f), r.id);
                Assert.That(r.inputs, Is.Not.Empty, r.id);
            }
            foreach (var p in T.parts)
            {
                Assert.That(p.name, Is.Not.Empty, p.id);
                Assert.That(p.look, Is.Not.Empty, p.id);
                Assert.That(p.mass, Is.GreaterThan(0f), p.id);
                Assert.That(p.seconds, Is.GreaterThan(0f), p.id);
                Assert.That(p.value, Is.GreaterThan(0f), p.id);
                Assert.That(p.inputs, Is.Not.Empty, p.id);
                Assert.That(p.effects, Is.Not.Empty, p.id + " does something fitted");
            }
        }

        [Test]
        public void RefiningTakesMineralsAndBuildingTakesRefinedMaterials()
        {
            foreach (var r in T.refined)
                foreach (var i in r.inputs)
                {
                    Assert.That(IndustryTable.AsMineral(i.item, out _), Is.True, $"{r.id} is refined from {i.item}, which isn't a mineral");
                    Assert.That(i.tonnes, Is.GreaterThan(0f), r.id);
                }
            foreach (var p in T.parts)
                foreach (var i in p.inputs)
                {
                    Assert.That(T.Refined(i.item), Is.Not.Null, $"{p.id} is built from {i.item}, which isn't a refined material");
                    Assert.That(i.tonnes, Is.GreaterThan(0f), p.id);
                }
        }

        [Test]
        public void APartsEffectsAreOnTheFrigatesNumbers()
        {
            foreach (var p in T.parts)
                foreach (var e in p.effects)
                {
                    Assert.That(IndustryTable.Stats, Does.Contain(e.stat), $"{p.id}: {e.stat}");
                    Assert.That(e.factor, Is.GreaterThan(0f).And.Not.EqualTo(1f), $"{p.id}: {e.stat}");
                }
            // every number a part can change is changed by some part
            foreach (var stat in IndustryTable.Stats)
                Assert.That(T.parts.Any(p => p.effects.Any(e => e.stat == stat)), Is.True, $"no part changes {stat}");
        }

        [Test]
        public void EveryMaterialIsRefinedAndEveryPartBuiltSomewhere()
        {
            foreach (var s in T.sites)
            {
                Assert.That(s.name, Is.Not.Empty);
                Assert.That(new[] { "surface", "moon", "orbit", "deepspace", "asteroid" }, Does.Contain(s.where), s.name);
                Assert.That(s.CanRefine || s.CanBuild, Is.True, $"{s.name} does nothing");
                foreach (var id in s.refines) if (id != "*") Assert.That(T.Refined(id), Is.Not.Null, $"{s.name} refines {id}");
                foreach (var id in s.builds) if (id != "*") Assert.That(T.Part(id), Is.Not.Null, $"{s.name} builds {id}");
                Assert.That(s.description, Is.Not.Empty, s.name);
            }
            foreach (var r in T.refined) Assert.That(T.sites.Any(s => s.Refines(r.id)), Is.True, $"{r.id} is refined nowhere");
            foreach (var p in T.parts) Assert.That(T.sites.Any(s => s.Builds(p.id)), Is.True, $"{p.id} is built nowhere");
            // in every kind of place: on a planet, a moon and an asteroid, in orbit and in deep space
            foreach (var where in new[] { "surface", "moon", "orbit", "deepspace", "asteroid" })
                Assert.That(T.sites.Any(s => s.where == where), Is.True, $"no site in {where}");
        }

        [Test]
        public void EveryMaterialAndPartHasAPicture()
        {
            foreach (var id in T.refined.Select(r => r.id).Concat(T.parts.Select(p => p.id)))
                Assert.That(IndustryTable.Icon(id), Is.Not.Null, $"Resources/Industry/Icons/{id}.png");
        }

        [Test]
        public void NamesAmountsAndValues()
        {
            Assert.That(T.Kind("Iron"), Is.EqualTo(ItemKind.Raw));
            Assert.That(T.Kind("SteelPlate"), Is.EqualTo(ItemKind.Refined));
            Assert.That(T.Kind("ArmourPlating"), Is.EqualTo(ItemKind.Part));
            Assert.That(T.Kind("Nonsense"), Is.EqualTo(ItemKind.None));
            Assert.That(T.Name("Iron"), Is.EqualTo(Minerals.Name(Mineral.Iron)));
            Assert.That(T.Value("Iron"), Is.EqualTo(Minerals.Value(Mineral.Iron)));
            Assert.That(T.Mass("ArmourPlating", 2f), Is.EqualTo(2f * T.Part("ArmourPlating").mass));
            Assert.That(T.Mass("SteelPlate", 2f), Is.EqualTo(2f));
            Assert.That(T.Amount("ArmourPlating", 1f), Is.EqualTo("1 part"));
            Assert.That(T.Amount("SteelPlate", 12.5f), Is.EqualTo("12.5 t"));
        }
    }

    /// <summary>The hold carrying refined goods and parts as well as ore.</summary>
    public class CargoGoodsTests
    {
        GameObject go;
        CargoHold hold;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("Hold");
            hold = go.AddComponent<CargoHold>();
            hold.capacity = 100f;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(go);

        [Test]
        public void RefinedGoodsAndPartsTakeRoomByMass()
        {
            Assert.That(hold.Load("SteelPlate", 30f), Is.EqualTo(30f));
            Assert.That(hold.Used, Is.EqualTo(30f).Within(1e-3f));
            // armour plating weighs 40 t a part: one fits in the 70 t left, not two, and never half of one
            Assert.That(hold.RoomFor("ArmourPlating"), Is.EqualTo(1f));
            Assert.That(hold.Load("ArmourPlating", 3f), Is.EqualTo(1f));
            Assert.That(hold.Amount("ArmourPlating"), Is.EqualTo(1f));
            Assert.That(hold.Used, Is.EqualTo(70f).Within(1e-3f));
            Assert.That(hold.Load("Iron", 50f), Is.EqualTo(30f), "only as much ore as there's room for");
            Assert.That(hold.Full, Is.True);
            Assert.That(hold.TotalMined, Is.EqualTo(0f), "brought from storage, not mined");
        }

        [Test]
        public void UnloadingGivesBackWhatIsThere()
        {
            hold.Load("CopperWire", 12f);
            hold.Add(Mineral.Iron, 20f);
            Assert.That(hold.Unload("CopperWire", 5f), Is.EqualTo(5f));
            Assert.That(hold.Unload("CopperWire", 50f), Is.EqualTo(7f));
            Assert.That(hold.Unload("Iron", 8f), Is.EqualTo(8f));
            Assert.That(hold[Mineral.Iron], Is.EqualTo(12f).Within(1e-3f));
            Assert.That(hold.Used, Is.EqualTo(12f).Within(1e-3f));
            Assert.That(hold.Unload("Nonsense", 1f), Is.EqualTo(0f));
        }

        [Test]
        public void SellingSellsTheOreAndKeepsTheGoods()
        {
            hold.Add(Mineral.Iron, 20f);
            hold.Load("Coolant", 10f);
            float cr = hold.SellAll(out float tonnes);
            Assert.That(tonnes, Is.EqualTo(20f).Within(1e-3f));
            Assert.That(cr, Is.EqualTo(20f * Minerals.Value(Mineral.Iron)).Within(0.01f));
            Assert.That(hold.Amount("Coolant"), Is.EqualTo(10f));
            Assert.That(hold.Used, Is.EqualTo(10f).Within(1e-3f));
        }

        [Test]
        public void TheContentsComeBackAsSaved()
        {
            hold.Add(Mineral.Gold, 2f);
            hold.Load("Magnets", 3f);
            hold.Load("ShieldEmitter", 1f);
            var saved = hold.Contents();
            var other = new GameObject("Other").AddComponent<CargoHold>();
            try
            {
                other.capacity = 50f;
                other.Restore(saved, 1234f);
                Assert.That(other[Mineral.Gold], Is.EqualTo(2f));
                Assert.That(other.Amount("Magnets"), Is.EqualTo(3f));
                Assert.That(other.Amount("ShieldEmitter"), Is.EqualTo(1f));
                Assert.That(other.Used, Is.EqualTo(hold.Used).Within(1e-3f));
                Assert.That(other.Credits, Is.EqualTo(1234f));
            }
            finally { Object.DestroyImmediate(other.gameObject); }
        }
    }

    /// <summary>The sites' storage and production lines.</summary>
    public class IndustryLedgerTests
    {
        const string Site = "Ochre Foundry";      // refines and builds everything
        const string GasWorks = "Jovara Gas Works";

        [SetUp]
        public void SetUp() { IndustryLedger.Clear(); IndustryLedger.SpeedUp = 1f; }

        [TearDown]
        public void TearDown() { IndustryLedger.Clear(); IndustryLedger.SpeedUp = 1f; }

        static Stock StockOf(string site) => IndustryLedger.Site(site).stock;

        [Test]
        public void ARefiningJobTakesItsInputsAndMakesItsOutput()
        {
            var steel = IndustryTable.Get().Refined("SteelPlate");
            StockOf(Site).Add("Iron", 12f);
            StockOf(Site).Add("Carbon", 3f);
            Assert.That(IndustryLedger.Queue(Site, "SteelPlate", false, out string why), Is.True, why);
            Assert.That(StockOf(Site).Amount("Iron"), Is.EqualTo(12f - 5f).Within(1e-3f), "taken when queued");
            IndustryLedger.Advance(steel.seconds * 0.5f);
            Assert.That(StockOf(Site).Amount("SteelPlate"), Is.EqualTo(0f), "not done yet");
            Assert.That(IndustryLedger.Site(Site).refining[0].Progress, Is.EqualTo(0.5f).Within(0.01f));
            IndustryLedger.Advance(steel.seconds * 0.6f);
            Assert.That(StockOf(Site).Amount("SteelPlate"), Is.EqualTo(steel.makes));
            Assert.That(IndustryLedger.Site(Site).refining, Is.Empty);
        }

        [Test]
        public void NothingIsQueuedWithoutItsInputsOrWhereItIsntMade()
        {
            Assert.That(IndustryLedger.Queue(Site, "SteelPlate", false, out string why), Is.False);
            Assert.That(why, Does.Contain("needs"));
            StockOf(GasWorks).Add("Iron", 100f);
            StockOf(GasWorks).Add("Carbon", 100f);
            Assert.That(IndustryLedger.Queue(GasWorks, "SteelPlate", false, out why), Is.False, "the gas works doesn't refine steel");
            Assert.That(IndustryLedger.Queue(GasWorks, "ArmourPlating", true, out why), Is.False, "nor build parts");
            Assert.That(IndustryLedger.Queue("Nowhere", "SteelPlate", false, out why), Is.False);
        }

        [Test]
        public void CancellingPutsTheInputsBack()
        {
            StockOf(Site).Add("SteelPlate", 30f);
            StockOf(Site).Add("TitaniumBillet", 8f);
            Assert.That(IndustryLedger.Queue(Site, "ArmourPlating", true, out string why), Is.True, why);
            Assert.That(StockOf(Site).Amount("SteelPlate"), Is.EqualTo(0f));
            Assert.That(IndustryLedger.Cancel(Site, true), Is.True);
            Assert.That(StockOf(Site).Amount("SteelPlate"), Is.EqualTo(30f));
            Assert.That(StockOf(Site).Amount("TitaniumBillet"), Is.EqualTo(8f));
            Assert.That(IndustryLedger.Cancel(Site, true), Is.False, "nothing left to cancel");
        }

        [Test]
        public void JobsRunOneAfterAnotherAndTheTwoLinesTogether()
        {
            var t = IndustryTable.Get();
            StockOf(Site).Add("Aluminium", 10f);
            StockOf(Site).Add("SteelPlate", 10f);
            StockOf(Site).Add("AluminiumSheet", 25f);
            IndustryLedger.Queue(Site, "AluminiumSheet", false, out _);
            IndustryLedger.Queue(Site, "AluminiumSheet", false, out _);
            IndustryLedger.Queue(Site, "CargoPod", true, out _);
            float sheet = t.Refined("AluminiumSheet").seconds, pod = t.Part("CargoPod").seconds;
            int done = 0;
            IndustryLedger.Finished += (s, item, n) => done++;
            IndustryLedger.Advance(sheet * 2f + 0.01f);   // both batches, in the one step
            Assert.That(StockOf(Site).Amount("AluminiumSheet"), Is.EqualTo(t.Refined("AluminiumSheet").makes * 2f).Within(1e-3f));
            Assert.That(StockOf(Site).Amount("CargoPod"), Is.EqualTo(sheet * 2f >= pod ? 1f : 0f), "the factory ran meanwhile");
            IndustryLedger.Advance(pod);
            Assert.That(StockOf(Site).Amount("CargoPod"), Is.EqualTo(1f));
            Assert.That(done, Is.EqualTo(3));
        }

        [Test]
        public void TheQueueHasALimit()
        {
            StockOf(Site).Add("Aluminium", 1000f);
            int n = 0;
            while (IndustryLedger.Queue(Site, "AluminiumSheet", false, out _) && n < 100) n++;
            Assert.That(n, Is.EqualTo(IndustryLedger.QueueLimit));
        }

        [Test]
        public void SpeedingUpRunsTheLinesFaster()
        {
            StockOf(Site).Add("Copper", 4f);
            IndustryLedger.Queue(Site, "CopperWire", false, out _);
            IndustryLedger.SpeedUp = 100f;
            IndustryLedger.Advance(IndustryTable.Get().Refined("CopperWire").seconds / 99f);
            Assert.That(StockOf(Site).Amount("CopperWire"), Is.EqualTo(4f));
        }

        [Test]
        public void EverythingComesBackAsSaved()
        {
            StockOf(Site).Add("Iron", 40f);
            StockOf(Site).Add("Carbon", 4f);
            IndustryLedger.Queue(Site, "SteelPlate", false, out _);
            IndustryLedger.Advance(5f);
            IndustryLedger.FittedTo("Sentinel").Add("CargoPod");
            var (sites, fitted) = IndustryLedger.Snapshot();
            var json = IndustrySave.ToJson(new IndustrySave.Data { sites = sites, fittings = fitted, credits = 99f });
            IndustryLedger.Clear();
            var back = IndustrySave.FromJson(json);
            IndustryLedger.Restore(back.sites, back.fittings);
            Assert.That(back.credits, Is.EqualTo(99f));
            Assert.That(StockOf(Site).Amount("Iron"), Is.EqualTo(35f).Within(1e-3f));
            var job = IndustryLedger.Site(Site).refining.Single();
            Assert.That(job.recipe, Is.EqualTo("SteelPlate"));
            Assert.That(job.left, Is.EqualTo(job.total - 5f).Within(1e-3f));
            Assert.That(IndustryLedger.FittedTo("Sentinel"), Is.EqualTo(new[] { "CargoPod" }));
        }

        [Test]
        public void TheSaveIsWrittenAndReadBack()
        {
            string path = Path.Combine(Path.GetTempPath(), $"carrier_industry_test_{System.Guid.NewGuid():N}.json");
            try
            {
                var d = new IndustrySave.Data { credits = 7f };
                d.hold.Add(new Stock.Entry { id = "Iron", amount = 3f });
                Assert.That(IndustrySave.Write(d, path), Is.True);
                var back = IndustrySave.Read(path);
                Assert.That(back.credits, Is.EqualTo(7f));
                Assert.That(back.hold.Single().id, Is.EqualTo("Iron"));
                Assert.That(back.version, Is.EqualTo(IndustrySave.Version));
                Assert.That(IndustrySave.Read(path + ".missing"), Is.Null);
                // written again over the first: replaced whole
                d.credits = 8f;
                Assert.That(IndustrySave.Write(d, path), Is.True);
                Assert.That(IndustrySave.Read(path).credits, Is.EqualTo(8f));
                Assert.That(File.Exists(path + ".tmp"), Is.False);
                // caught between writing the new one and putting it in place: the .tmp is read
                File.Move(path, path + ".tmp");
                Assert.That(IndustrySave.Read(path)?.credits, Is.EqualTo(8f));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            }
        }
    }

    /// <summary>Parts fitted to a frigate changing its numbers, and coming off again.</summary>
    public class ShipFittingsTests
    {
        GameObject go;
        Health health;
        CargoHold hold;
        ShipFittings fit;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("Ship");
            health = go.AddComponent<Health>();
            health.maxHealth = 1000f;
            hold = go.AddComponent<CargoHold>();
            hold.capacity = 500f;
            fit = go.AddComponent<ShipFittings>();
            fit.slots = 2;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(go);

        static float FactorOf(string part, string stat) => IndustryTable.Get().Part(part).effects.First(e => e.stat == stat).factor;

        [Test]
        public void APartChangesTheNumbersAndTakingItOffPutsThemBack()
        {
            Assert.That(fit.Fit("CargoPod"), Is.True);
            Assert.That(hold.capacity, Is.EqualTo(500f * FactorOf("CargoPod", "cargo")).Within(0.01f));
            Assert.That(fit.Fit("ArmourPlating"), Is.True);
            Assert.That(health.maxHealth, Is.EqualTo(1000f * FactorOf("ArmourPlating", "hull")).Within(0.01f));
            Assert.That(fit.Unfit("CargoPod"), Is.True);
            Assert.That(hold.capacity, Is.EqualTo(500f).Within(0.01f));
            Assert.That(fit.Value("hull"), Is.EqualTo(health.maxHealth).Within(0.01f));
            Assert.That(fit.Base("hull"), Is.EqualTo(1000f));
        }

        [Test]
        public void EachPartOnceAndNoMoreThanTheSlots()
        {
            Assert.That(fit.Fit("CargoPod"), Is.True);
            Assert.That(fit.CanFit("CargoPod", out string why), Is.False);
            Assert.That(why, Does.Contain("already"));
            Assert.That(fit.Fit("ArmourPlating"), Is.True);
            Assert.That(fit.Fit("ShieldEmitter"), Is.False, "two slots");
            Assert.That(fit.Free, Is.EqualTo(0));
            Assert.That(fit.Fit("Nonsense"), Is.False);
        }

        [Test]
        public void TwoPartsOnOneNumberMultiply()
        {
            fit.slots = 3;
            fit.Fit("ArmourPlating");
            fit.Fit("CompositeArmour");
            Assert.That(health.maxHealth, Is.EqualTo(1000f * FactorOf("ArmourPlating", "hull") * FactorOf("CompositeArmour", "hull")).Within(0.01f));
        }

        [Test]
        public void ACargoPodComesOffOnlyIfWhatIsAboardStillFits()
        {
            fit.Fit("CargoPod");
            Assert.That(hold.Load("SteelPlate", 600f), Is.EqualTo(600f), "more than the 500 t built hold");
            Assert.That(fit.CanUnfit("CargoPod", out string why), Is.False);
            Assert.That(why, Does.Contain("aboard"));
            Assert.That(fit.Unfit("CargoPod"), Is.False);
            Assert.That(fit.IsFitted("CargoPod"), Is.True);
            hold.Unload("SteelPlate", 150f);
            Assert.That(fit.Unfit("CargoPod"), Is.True, "450 t fits the hold without it");
            Assert.That(hold.capacity, Is.EqualTo(500f).Within(0.01f));
        }

        [Test]
        public void WhatWasSavedIsFittedAgain()
        {
            fit.SetFitted(new[] { "CargoPod", "CargoPod", "Nonsense", "ArmourPlating", "ShieldEmitter" });
            Assert.That(fit.Fitted, Is.EqualTo(new[] { "CargoPod", "ArmourPlating" }), "once each, known parts, up to the slots");
        }
    }
}

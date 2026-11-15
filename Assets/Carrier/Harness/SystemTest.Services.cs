using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using K = UnityEngine.InputSystem.Key;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The services mode (-servicestest): docking at a station puts the game in services mode, its
    /// screen offering what the place does; at a trading station (the Highport) the trade page
    /// sells what is aboard at its worth and buys at a markup, and the crew page drops crew off
    /// (they join those waiting to sign on); a refinery's trade page buys minerals only; leaving
    /// puts the game back in the mode it was in.
    /// </summary>
    public partial class SystemTest
    {
        bool servicesOnly;

        IEnumerator ServicesTest(Keyboard kb)
        {
            int checks = 0, passed = 0;
            void Check(bool ok, string what)
            {
                checks++;
                if (ok) passed++;
                Log($"{(ok ? "ok" : "FAIL")}: {what}");
            }
            var hull = cc.GetComponent<Health>();
            if (hull != null) hull.cannotDie = true;
            var dock = Docking.Instance;
            var panel = IndustryPanel.Instance;
            var hold = demo.Cargo;
            var crew = cc.GetComponent<Crew>();
            if (dock == null || panel == null || hold == null) { Log("FAIL: services (no docking, services screen or hold)"); yield break; }
            var table = IndustryTable.Get();

            // ---- docking at the Highport: services mode, the trade and crew pages
            yield return Key(kb, K.Digit2);   // (mining mode, to come back to)
            yield return WarpTo(kb, "Thalassa Highport", "sv_highport");
            var hp = Installation.All.FirstOrDefault(i => i.displayName == "Thalassa Highport");
            yield return FlyTo(hp.BerthPosition, hp.dockRange * 0.85f, 60f);
            cc.throttle = 0f;
            hold.Add(Mineral.Iron, 60f);
            hold.Add(Mineral.Platinum, 3f);
            yield return new WaitForSeconds(drive.recentHitSeconds + 0.5f);
            yield return Key(kb, K.K);
            float until = Time.time + 10f;
            while (Time.time < until && !Docking.Docked) yield return null;
            yield return new WaitForSeconds(1f);
            Check(Docking.Docked && Modes.Is(ModeOfPlay.Services), $"docked at the Highport: {Modes.Name(Modes.Current)} mode");
            if (!IndustryPanel.IsOpen) yield return Key(kb, K.Tab);
            Check(IndustryPanel.IsOpen, $"the services screen is up ({panel.Current})");
            panel.Show(IndustryPanel.Page.Trade);
            yield return null;
            Check(panel.Current == IndustryPanel.Page.Trade, "the Highport has a trade page");
            Shot("sv_trade");

            // sell the ore at its worth
            float worth = hold.Value, credits0 = hold.Credits;
            float got = panel.SellAllOre();
            Check(hold.OreTonnes < 0.05f && Mathf.Abs(hold.Credits - credits0 - worth) < 1f,
                  $"the ore sold at its worth: {worth:N0} cr ({credits0:N0} -> {hold.Credits:N0} cr)");

            // buy a part, at the markup
            string partId = "ArmourPlating";
            float price = table.Value(partId) * IndustryPanel.BuyMarkup;
            if (hold.Credits < price * 2f) hold.Earn(price * 2f);
            float c1 = hold.Credits, have = hold.Amount(partId);
            panel.Act("*buy:" + partId, IndustryPanel.Action("buy"));
            Check(hold.Amount(partId) == have + 1f && Mathf.Abs(c1 - hold.Credits - price) < 1f,
                  $"bought one {table.Name(partId).ToLower()} for {price:N0} cr ({table.Value(partId):N0} cr worth, x{IndustryPanel.BuyMarkup})");
            // and sell it back, at its worth
            float c2 = hold.Credits;
            panel.Act("*sell:" + partId, IndustryPanel.Action("sell"));
            Check(hold.Amount(partId) == have && Mathf.Abs(hold.Credits - c2 - table.Value(partId)) < 1f, $"and sold it back for {table.Value(partId):N0} cr");

            // drop crew off: they join those waiting to sign on
            if (crew != null)
            {
                panel.Show(IndustryPanel.Page.Crew);
                yield return null;
                int aboard = crew.Aboard;
                float pool = IndustryLedger.Pool("Thalassa Highport");
                panel.Act("*d10", IndustryPanel.Action("dropoff"));
                Check(crew.Aboard == aboard - 10 && IndustryLedger.Pool("Thalassa Highport") >= pool + 9.9f,
                      $"ten crew dropped off: {aboard} -> {crew.Aboard} aboard; waiting to sign on {pool:F0} -> {IndustryLedger.Pool("Thalassa Highport"):F0}");
                // and recruited back
                if (hold.Credits < 10 * CrewTable.Get().recruitCost) hold.Earn(10 * CrewTable.Get().recruitCost);
                panel.Act("*r10", IndustryPanel.Action("recruit"));
                Check(crew.Aboard == aboard, $"and recruited back ({crew.Aboard} aboard)");
            }
            panel.Close();
            yield return null;

            // leaving: back to the mode before
            yield return Key(kb, K.K);
            until = Time.time + 10f;
            while (Time.time < until && Docking.Busy) yield return null;
            yield return new WaitForSeconds(0.5f);
            Check(!Docking.Busy && Modes.Is(ModeOfPlay.Mining), $"undocked: back in {Modes.Name(Modes.Current)} mode");
            cc.throttle = 0f;

            // ---- a refinery's trade page buys minerals only
            yield return WarpTo(kb, "Belt Smelter", "sv_smelter");
            var sm = Installation.All.FirstOrDefault(i => i.displayName == "Belt Smelter");
            if (sm != null && sm.canDock)
            {
                yield return FlyTo(sm.BerthPosition, sm.dockRange * 0.85f, 60f);
                cc.throttle = 0f;
                hold.Add(Mineral.Iron, 20f);
                hold.Load("SteelPlate", 5f);
                yield return new WaitForSeconds(drive.recentHitSeconds + 0.5f);
                yield return Key(kb, K.K);
                until = Time.time + 10f;
                while (Time.time < until && !Docking.Docked) yield return null;
                yield return new WaitForSeconds(1f);
                if (!IndustryPanel.IsOpen) panel.Open(IndustryPanel.Page.Trade); else panel.Show(IndustryPanel.Page.Trade);
                yield return null;
                var rows = panel.RowIds.ToList();
                bool sellsIron = rows.Contains("*sell:Iron"), sellsSteel = rows.Contains("*sell:SteelPlate"), buys = rows.Any(r => r.StartsWith("*buy:"));
                Check(panel.Current == IndustryPanel.Page.Trade && sellsIron && !sellsSteel && !buys,
                      $"the Belt Smelter's trade page buys the iron, not the steel plate, and sells nothing ({rows.Count} rows)");
                Shot("sv_smelter_trade");
                panel.Close();
                yield return null;
                yield return Key(kb, K.K);
                until = Time.time + 10f;
                while (Time.time < until && Docking.Busy) yield return null;
            }
            else Check(false, "the Belt Smelter is somewhere to dock");

            if (hull != null) hull.cannotDie = false;
            Log($"{(passed == checks ? "PASS" : "FAIL")}: services ({passed} of {checks})");
        }
    }
}

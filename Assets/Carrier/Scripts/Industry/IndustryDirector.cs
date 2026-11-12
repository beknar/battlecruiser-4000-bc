using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Runs the industry in the level: reads the save once when the game starts, runs every
    /// site's lines each frame, puts the saved hold and the class's fitted parts on the player's
    /// frigate (again when the frigate is swapped), tells the player when a job is done, and saves
    /// a few seconds after anything changes, every minute, and on quitting.
    /// </summary>
    public class IndustryDirector : MonoBehaviour
    {
        public CarrierController carrier;
        [Tooltip("Seconds after a change before it is saved.")]
        public float saveDelay = 3f;

        public static IndustryDirector Instance { get; private set; }

        // (kept across a restart of the level: the hold as it was, the credits)
        static bool loaded;
        static List<Stock.Entry> heldContents;
        static float heldCredits;
        static bool haveHeld;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            loaded = false;
            heldContents = null;
            heldCredits = 0f;
            haveHeld = false;
        }

        CarrierController attached;
        CargoHold hold;
        ShipFittings fittings;
        float saveDue = -1f, nextRegular;

        public CargoHold Hold => hold;
        public ShipFittings Fittings => fittings;
        public string Frigate => attached != null ? FrigateClass.Of(attached) : "";

        void Awake() => Instance = this;

        void Start()
        {
            if (!loaded)
            {
                loaded = true;
                var d = IndustrySave.NewGame ? null : IndustrySave.Read();
                if (d != null)
                {
                    IndustryLedger.Restore(d.sites, d.fittings);
                    heldContents = d.hold;
                    heldCredits = d.credits;
                    haveHeld = true;
                    Debug.Log($"[Industry] loaded {IndustrySave.LastPath}: {d.sites.Count} sites, {d.hold.Count} kinds aboard, {d.credits:N0} cr");
                }
            }
            IndustryLedger.Finished += OnFinished;
            IndustryLedger.Changed += Dirty;
            Attach(carrier);
            nextRegular = Time.unscaledTime + 60f;
        }

        void OnDestroy()
        {
            IndustryLedger.Finished -= OnFinished;
            IndustryLedger.Changed -= Dirty;
            Detach();
            Save();
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit() => Save();

        void Attach(CarrierController cc)
        {
            Detach();
            attached = cc;
            if (cc == null) return;
            hold = cc.GetComponent<CargoHold>();
            fittings = cc.GetComponent<ShipFittings>();
            if (fittings == null) fittings = cc.gameObject.AddComponent<ShipFittings>();
            fittings.SetFitted(IndustryLedger.FittedTo(FrigateClass.Of(cc)));
            fittings.Changed += FittingsChanged;
            if (hold != null)
            {
                if (haveHeld) hold.Restore(heldContents, heldCredits);
                hold.Changed += Dirty;
            }
        }

        void Detach()
        {
            if (attached == null) return;
            Remember();
            if (hold != null) hold.Changed -= Dirty;
            if (fittings != null) fittings.Changed -= FittingsChanged;
            attached = null;
            hold = null;
            fittings = null;
        }

        /// <summary>Note the hold as it is now (for the save, and for the next frigate or level).</summary>
        void Remember()
        {
            if (hold == null) return;
            heldContents = hold.Contents();
            heldCredits = hold.Credits;
            haveHeld = true;
        }

        void FittingsChanged()
        {
            if (fittings == null || attached == null) return;
            var list = IndustryLedger.FittedTo(FrigateClass.Of(attached));
            list.Clear();
            list.AddRange(fittings.Fitted);
            Dirty();
        }

        void Dirty()
        {
            if (saveDue < 0f) saveDue = Time.unscaledTime + saveDelay;
        }

        void Update()
        {
            if (carrier != attached) Attach(carrier);
            IndustryLedger.Advance(Time.deltaTime);
            float now = Time.unscaledTime;
            if ((saveDue >= 0f && now >= saveDue) || now >= nextRegular) Save();
        }

        /// <summary>Save now (returns false if this run doesn't save, or it failed).</summary>
        public bool Save()
        {
            saveDue = -1f;
            nextRegular = Time.unscaledTime + 60f;
            Remember();
            var (sites, fitted) = IndustryLedger.Snapshot();
            var d = new IndustrySave.Data
            {
                sites = sites,
                fittings = fitted,
                hold = heldContents ?? new List<Stock.Entry>(),
                credits = heldCredits,
            };
            return IndustrySave.Write(d);
        }

        void OnFinished(string site, string item, float amount)
        {
            var table = IndustryTable.Get();
            Notices.Post($"{site}: {table.Amount(item, amount)} of {table.Name(item).ToLower()} ready");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
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
        // (the ship it is attached to, held as a plain object: swapping the frigate points every
        // Unity object reference in the level at the new ship, these included, so they can't tell)
        object attachedShip;
        CargoHold hold;
        ShipFittings fittings;
        Crew crew;
        float saveDue = -1f, nextRegular;
        bool holdDirty;

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
                    IndustryLedger.RestorePeople(d.pools, d.crews);
                    WorldState.Restore(d.world);
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
            Detach(true);
            Save();
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit() => Save();

        void Attach(CarrierController cc)
        {
            // (a swapped-in frigate: the old one's hold was noted the frame before, and its
            // references here already point at the new ship, so nothing more is noted)
            Detach(false);
            attached = cc;
            attachedShip = cc;
            if (cc == null) return;
            hold = cc.GetComponent<CargoHold>();
            fittings = cc.GetComponent<ShipFittings>();
            if (fittings == null) fittings = cc.gameObject.AddComponent<ShipFittings>();
            var saved = IndustryLedger.FittedTo(FrigateClass.Of(cc));
            fittings.SetFitted(saved);
            // parts saved as fitted that it can't take now (fewer slots, or a part that no longer
            // exists) aren't lost: they go into a factory's storage
            var dropped = saved.Where(id => !fittings.IsFitted(id)).ToList();
            if (dropped.Count > 0)
            {
                var table = IndustryTable.Get();
                var factory = table.sites.FirstOrDefault(s => s.CanBuild);
                foreach (var id in dropped)
                    if (factory != null && table.Part(id) != null) IndustryLedger.Site(factory.name).stock.Add(id, 1f);
                saved.Clear();
                saved.AddRange(fittings.Fitted);
                if (factory != null) Notices.Post($"{dropped.Count} part(s) no longer fit: put into storage at {factory.name}");
                Dirty();
            }
            fittings.Changed += FittingsChanged;
            // its crew as they were (a frigate class keeps the crew it had: repairs don't bring
            // the dead back)
            crew = cc.GetComponent<Crew>();
            if (crew != null)
            {
                var had = IndustryLedger.CrewOf(FrigateClass.Of(cc));
                if (had != null) crew.SetAboard(had.Value);
                crew.Changed += CrewChanged;
            }
            if (hold != null)
            {
                if (haveHeld) hold.Restore(heldContents, heldCredits);
                hold.Changed += HoldChanged;
            }
        }

        void Detach(bool remember)
        {
            if (attachedShip == null) return;
            if (remember) Remember();
            if (hold != null) hold.Changed -= HoldChanged;
            if (fittings != null) fittings.Changed -= FittingsChanged;
            if (crew != null) crew.Changed -= CrewChanged;
            crew = null;
            attached = null;
            attachedShip = null;
            hold = null;
            fittings = null;
        }

        void CrewChanged()
        {
            if (crew == null || attached == null) return;
            IndustryLedger.SetCrew(FrigateClass.Of(attached), crew.Current);
        }

        void HoldChanged()
        {
            holdDirty = true;
            Dirty();
        }

        /// <summary>Note the hold as it is now (for the save, and for the next frigate or level).
        /// Done each frame it changes, so the note is never more than a frame old, even when the
        /// level goes and the hold with it.</summary>
        void Remember()
        {
            holdDirty = false;
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
            if (!ReferenceEquals(carrier, attachedShip)) Attach(carrier);
            if (holdDirty) Remember();
            IndustryLedger.Advance(Time.deltaTime);
            WorldState.Advance(Time.deltaTime);
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
            var (pools, crews) = IndustryLedger.PeopleSnapshot();
            var d = new IndustrySave.Data
            {
                sites = sites,
                fittings = fitted,
                pools = pools,
                crews = crews,
                world = WorldState.Snapshot(),
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

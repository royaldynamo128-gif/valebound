using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valebound.NPC
{
    /// <summary>
    /// Mission giver: stands at a fixed post, shows a name tag + marker, offers
    /// a dialogue tree with mission accept. Uses PlayerInteraction.IInteractable.
    /// </summary>
    public class MissionGiverNPC : MonoBehaviour, PlayerInteraction.IInteractable
    {
        [Serializable]
        public class DialogueLine
        {
            public string speaker;
            [TextArea(1, 4)] public string text;
        }

        [Header("Identity")]
        [SerializeField] private string npcId;
        [SerializeField] private string displayName;
        [SerializeField] private TextMesh nameTag;

        [Header("Dialogue")]
        [SerializeField] private DialogueLine[] greetingLines;
        [SerializeField] private DialogueLine[] busyLines;
        [SerializeField] private DialogueLine[] doneLines;

        [Header("Mission")]
        [SerializeField] private MissionDef[] offers;

        public string NpcId => npcId;
        public string Prompt => HasAvailableMission() ? $"Talk to {displayName}" : $"Chat with {displayName}";

        /// <summary>Editor WorldBuilder sets the display name at bake time.</summary>
        public void SetFields(string vistaName) => displayName = vistaName;

        private bool talked;

        private void Start()
        {
            if (nameTag != null)
            {
                nameTag.text = displayName;
                nameTag.color = new Color(1f, 0.84f, 0.3f);
            }
        }

        private bool HasAvailableMission()
        {
            if (offers == null) return false;
            var mm = MissionManager.Instance;
            if (mm == null) return false;
            foreach (var m in offers)
                if (mm.CanOffer(m) && !mm.OfferedMissions.Contains(m) && !mm.ActiveMissions.Contains(m))
                    return true;
            return false;
        }

        public bool CanInteract(PlayerController player) => true;

        public void Interact(PlayerController player)
        {
            var mm = MissionManager.Instance;
            var ui = UI.UIManager.Instance;
            if (mm == null || ui == null) return;

            // pick first offerable mission
            MissionDef offer = null;
            foreach (var m in offers)
                if (mm.CanOffer(m) && !mm.OfferedMissions.Contains(m) && !mm.ActiveMissions.Contains(m)) { offer = m; break; }

            if (offer != null)
            {
                ui.ShowDialogue(displayName, greetingLines, () =>
                {
                    mm.OfferMission(offer);
                    ui.ShowMissionOfferPanel(offer, accept: () => mm.AcceptMission(offer));
                });
            }
            else
            {
                var lines = talked ? doneLines : busyLines;
                if ((lines == null || lines.Length == 0) && greetingLines != null) lines = greetingLines;
                ui.ShowDialogue(displayName, lines, null);
                talked = true;
            }
        }
    }

    /// <summary>
    /// Collectible signal shard: floats/spins, collected on interact or drive-through.
    /// </summary>
    public class SignalShard : MonoBehaviour, PlayerInteraction.IInteractable
    {
        [SerializeField] private int shardIndex = -1;
        [SerializeField] private float bobSpeed = 1.6f;
        [SerializeField] private float bobHeight = 0.25f;
        [SerializeField] private float spinSpeed = 90f;
        [SerializeField] private ParticleSystem collectPfx;

        public string Prompt => "Collect Signal Shard";

        /// <summary>Editor WorldBuilder sets the shard index at bake time.</summary>
        public void SetIndex(int index) => shardIndex = index;

        private Vector3 basePos;
        private bool collected;

        private void Awake() { basePos = transform.position; }

        private void Update()
        {
            if (collected) return;
            Vector3 p = basePos;
            p.y += Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = p;
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        }

        public bool CanInteract(PlayerController player) => !collected;

        public void Interact(PlayerController player) => Collect();

        private void OnTriggerEnter(Collider other)
        {
            if (collected) return;
            if (other.GetComponentInParent<PlayerController>() != null ||
                other.GetComponentInParent<Vehicle.PlayerVehicle>() != null)
                Collect();
        }

        private void Collect()
        {
            if (collected) return;
            collected = true;
            if (collectPfx != null) collectPfx.Play();
            Systems.CollectibleManager.Instance?.NotifyCollected(shardIndex);
            Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/shard", 0.85f);
            Destroy(gameObject, 0.6f);
        }
    }

    /// <summary>
    /// Vista photo point: on interact, frames a "photo" (notification + xp).
    /// </summary>
    public class VistaPoint : MonoBehaviour, PlayerInteraction.IInteractable
    {
        [SerializeField] private string vistaName = "Vista Point";
        [SerializeField] private int xpReward = 40;

        public string Prompt => $"Photograph {vistaName}";

        /// <summary>Editor WorldBuilder sets the display name at bake time.</summary>
        public void SetFields(string name) => vistaName = name;

        private bool used;

        public bool CanInteract(PlayerController player) => !used;

        public void Interact(PlayerController player)
        {
            if (used) return;
            used = true;
            Systems.EconomyManager.Instance?.AddXp(xpReward);
            Systems.AchievementSystem.Instance?.Unlock("photographer");
            UI.UIManager.Instance?.ShowNotification("Photo captured",
                $"{vistaName} saved to your journal. +{xpReward} XP", 3.5f);
            Systems.AudioManager.Instance?.PlayOneShot("sfx/ui/camera", 0.8f);
            GetComponent<Collider>().enabled = false;
        }
    }
}

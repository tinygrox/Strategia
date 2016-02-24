﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using KSP;
using Strategies;
using Upgradeables;

namespace Strategia
{
    /// <summary>
    /// MonoBehaviour to show text for currency being gained/spent.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class CurrencyPopup : MonoBehaviour
    {
        private enum AnchorType
        {
            Transform,
            Fixed,
            None
        }

        public static CurrencyPopup Instance { get; private set; }

        private GUIStyle popupStyle = null;

        private UpgradeableFacility lastFacility = null;
        private float lastFacilityTime = 0.0f;
        private bool crewCorrectionRequired = false;
        private double correctionAmount = 0.0;

        private float lastPopupTime = 0.0f;

        const float DURATION = 2.5f;
        const float DELTA_TIME = 0.60f;
        static Color fundsColor = new Color(0xB4 / 255.0f, 0xD4 / 255.0f, 0x55 / 255.0f);
        static Color fundsNegativeColor = new Color(0xB4 / 255.0f, 0x42 / 255.0f, 0x3C / 255.0f);
        static Color reputationColor = new Color(0xE0 / 255.0f, 0xD5 / 255.0f, 0x03 / 255.0f);
        static Color reputationNegativeColor = new Color(0xE0 / 255.0f, 0x75 / 255.0f, 0x03 / 255.0f);
        static Color scienceColor = new Color(0x6D / 255.0f, 0xCF / 255.0f, 0xF6 / 255.0f);
        static Color scienceNegativeColor = new Color(0x6D / 255.0f, 0x46 / 255.0f, 0xF6 / 255.0f);

        class Popup
        {
            public Transform referencePosition;
            public Vector3 screenPosition = new Vector3();
            public int direction = 1;
            public Currency currency;
            public double amount;
            public string reason;
            public AnchorType anchorType;
            public bool isFacility = false;
            public bool isDelta = false;
            public TransactionReasons transactionReason;

            public float startTime = 0.0f;
            public bool initialized = true;
        }
        List<Popup> popups = new List<Popup>();

        void Awake()
        {
            // Destroy if we're not in a scene with a camera
            if (FlightCamera.fetch == null || FlightCamera.fetch.mainCamera == null)
            {
                DestroyImmediate(this);
                return;
            }

            CurrencyPopup current = FlightCamera.fetch.mainCamera.gameObject.GetComponent<CurrencyPopup>();
            if (current == null)
            {
                FlightCamera.fetch.mainCamera.gameObject.AddComponent<CurrencyPopup>();

                // Destroy this object - otherwise we'll have two
                DestroyImmediate(this);
                return;
            }
            else if (current != this)
            {
                // Nope, already got one, don't want it
                DestroyImmediate(this);
                return;
            }

            Instance = this;

            GameEvents.OnKSCFacilityUpgraded.Add(new EventData<UpgradeableFacility, int>.OnEvent(OnKSCFacilityUpgraded));
            GameEvents.OnKSCStructureRepairing.Add(new EventData<DestructibleBuilding>.OnEvent(OnKSCStructureRepairing));
            GameEvents.OnCrewmemberHired.Add(new EventData<ProtoCrewMember, int>.OnEvent(OnCrewHired));
            GameEvents.OnTechnologyResearched.Add(new EventData<GameEvents.HostTargetAction<RDTech, RDTech.OperationResult>>.OnEvent(OnTechResearched));
            GameEvents.Modifiers.OnCurrencyModified.Add(new EventData<CurrencyModifierQuery>.OnEvent(OnCurrencyModified));
        }
        
        void Destroy()
        {
            Instance = null;

            GameEvents.OnKSCFacilityUpgraded.Remove(new EventData<UpgradeableFacility, int>.OnEvent(OnKSCFacilityUpgraded));
            GameEvents.OnKSCStructureRepairing.Remove(new EventData<DestructibleBuilding>.OnEvent(OnKSCStructureRepairing));
            GameEvents.OnCrewmemberHired.Remove(new EventData<ProtoCrewMember, int>.OnEvent(OnCrewHired));
            GameEvents.OnTechnologyResearched.Remove(new EventData<GameEvents.HostTargetAction<RDTech, RDTech.OperationResult>>.OnEvent(OnTechResearched));
            GameEvents.Modifiers.OnCurrencyModified.Remove(new EventData<CurrencyModifierQuery>.OnEvent(OnCurrencyModified));
        }

        public void AddFacilityPopup(Currency currency, double amount, TransactionReasons transactionReason, string reason, bool isDelta)
        {
            AddPopup(currency, amount, transactionReason, reason, null, AnchorType.Transform, isDelta, true);
        }

        public void AddPopup(Currency currency, double amount, TransactionReasons transactionReason, string reason, bool isDelta)
        {
            AddPopup(currency, amount, transactionReason, reason, null, AnchorType.None, isDelta);
        }

        public void AddPopup(Currency currency, double amount, TransactionReasons transactionReason, string reason, Transform referencePosition, bool isDelta)
        {
            AddPopup(currency, amount, transactionReason, reason, referencePosition, AnchorType.Transform, isDelta);
        }

        private void AddPopup(Currency currency, double amount, TransactionReasons transactionReason, string reason, Transform referencePosition, AnchorType anchorType, bool isDelta, bool isFacility = false)
        {
            Popup popup = new Popup();
            popup.currency = currency;
            popup.amount = amount;
            popup.transactionReason = transactionReason;
            popup.reason = reason;
            popup.anchorType = anchorType;
            popup.referencePosition = referencePosition;
            popup.isFacility = isFacility;
            popup.isDelta = isDelta;
            popups.Add(popup);

            // Special stuff
            if (isFacility)
            {
                popup.initialized = false;
            }
        }

        void OnGUI()
        {
            SetupStyles();

            foreach (Popup popup in popups.OrderBy(p => p.isDelta ? 1 : 0).ToList())
            {
                // Set up the facility popup
                if (!popup.initialized && popup.isFacility)
                {
                    if (Time.time < lastFacilityTime + 1.0f && lastFacility != null)
                    {
                        popup.referencePosition = lastFacility.transform;
                        popup.initialized = true;
                    }
                    else
                    {
                        continue;
                    }
                }

                // Initialize popup time
                if (popup.startTime == 0.0)
                {
                    if (lastPopupTime + DELTA_TIME < Time.time)
                    {
                        popup.startTime = Time.time;
                        lastPopupTime = Time.time;
                    }
                    else
                    {
                        continue;
                    }
                }

                // Remove the popup after a time delay
                if (Time.time - popup.startTime > DURATION)
                {
                    popups.Remove(popup);
                    continue;
                }

                // Figure out positioning
                if (popup.anchorType == AnchorType.None)
                {
                    // Set it based on where the appropriate box is
                    if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
                    {
                        popup.direction = -1;
                        popup.anchorType = AnchorType.Fixed;
                        popup.screenPosition.y = Screen.height - 32.0f;
                        if (popup.currency == Currency.Funds)
                        {
                            popup.screenPosition.x = (float)Screen.width / 2.0f - 220.0f / ScreenSafeUI.VerticalRatio;
                        }
                        else if (popup.currency == Currency.Reputation)
                        {
                            popup.screenPosition.x = (float)Screen.width / 2.0f;
                        }
                        else
                            popup.screenPosition.x = (float)Screen.width / 2.0f + 220.0f / ScreenSafeUI.VerticalRatio;
                        {
                        }
                    }
                    else if (HighLogic.LoadedScene == GameScenes.FLIGHT && FlightGlobals.ActiveVessel != null)
                    {
                        popup.anchorType = AnchorType.Transform;
                        popup.referencePosition = FlightGlobals.ActiveVessel.transform;
                    }
                }

                if (popup.anchorType == AnchorType.Transform)
                {
                    // Sometimes this can happen if a scene is left with an active popup
                    if (popup.referencePosition == null)
                    {
                        popups.Remove(popup);
                        continue;
                    }

                    Camera camera = RDController.Instance != null ? UIManager.instance.uiCameras[0].camera : FlightCamera.fetch.mainCamera;
                    popup.screenPosition = camera.WorldToScreenPoint(popup.referencePosition.position);
                }

                // Set up position and alpha
                float alpha = Mathf.Clamp(Mathf.Lerp(1.0f, 0.0f, Mathf.InverseLerp(popup.startTime + DURATION - 0.35f, popup.startTime + DURATION, Time.time)), 0.0f, 1.0f);
                float yoffset = Mathf.Lerp(20.0f, 80.0f, Mathf.InverseLerp(popup.startTime, popup.startTime + DURATION, Time.time)) * popup.direction;
                Rect origin = new Rect(popup.screenPosition.x - 200f, Screen.height - popup.screenPosition.y - yoffset - 28f, 400f, 28f);

                // Discount stroke/outline effect
                string format = popup.currency == Currency.Funds ? "N0" : "N1";
                string text = CurrencySymbol(popup.currency) + (popup.amount >= 0.0 ? " +" : " ") + popup.amount.ToString(format) + " (" + popup.reason + ")";
                Color currencyColor = CurrencyColor(popup.currency, popup.amount);
                Color backgroundColor = BackgroundColor(currencyColor);
                foreach (int x in new int[] {-1, 1, 0})
                {
                    foreach (int y in new int[] {-1, 1, 0})
                    {
                        // Setup styles, position and alpha
                        Color c = (x == 0 && y == 0) ?  currencyColor : backgroundColor;
                        popupStyle.normal.textColor = new Color(c.r, c.g, c.b, alpha);
                        Rect rect = new Rect(origin.xMin + x, origin.yMin + y, origin.width, origin.height);

                        // Draw the text
                        GUI.Box(rect, text, popupStyle);
                    }
                }

            }
        }

        protected void SetupStyles()
        {
            if (popupStyle != null)
            {
                return;
            }

            // Use this to find the right font when KSP 1.1 comes out
            /*Font[] fonts = UnityEngine.Resources.FindObjectsOfTypeAll<Font>();
            foreach (Font f in fonts)
            {
                Debug.Log("font = " + f.name);
            }*/

            popupStyle = new GUIStyle(HighLogic.Skin.label)
            {
                normal =
                {
                    textColor = Color.black
                },
                margin = new RectOffset(),
                padding = new RectOffset(5, 0, 0, 0),
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
        }

        void OnKSCFacilityUpgraded(UpgradeableFacility facility, int level)
        {
            lastFacility = facility;
            lastFacilityTime = Time.time;

            Popup popup = popups.LastOrDefault();
            if (popup != null)
            {
                popup.referencePosition = facility.transform;
                popup.initialized = true;
            }
        }

        void OnKSCStructureRepairing(DestructibleBuilding building)
        {
            Popup popup = popups.LastOrDefault();
            if (popup != null)
            {
                popup.referencePosition = building.transform;
                popup.initialized = true;
            }
        }

        void OnCrewHired(ProtoCrewMember pcm, int count)
        {
            if (crewCorrectionRequired)
            {
                ScreenMessages.PostScreenMessage("<b><color=orange>Not enough Funds to hire new recruit due to strategies currently in effect.</color></b>", 5f, ScreenMessageStyle.UPPER_LEFT);

                // Remove the last popup
                if (popups.Any())
                {
                    popups.Remove(popups.Last());
                }

                // Set the crew back to an applicant
                pcm.type = ProtoCrewMember.KerbalType.Applicant;

                // Correct the funds
                Funding.Instance.AddFunds(correctionAmount - Funding.Instance.Funds, TransactionReasons.Strategies);

                // Force the astronaut complex GUI
                CMAstronautComplex ac = UnityEngine.Object.FindObjectOfType<CMAstronautComplex>();
                if (ac != null)
                {
                    MethodInfo availableListMethod = typeof(CMAstronautComplex).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).
                        Where(mi => mi.Name == "CreateAvailableList").First();
                    availableListMethod.Invoke(ac, new object[] { });

                    MethodInfo applicantListMethod = typeof(CMAstronautComplex).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).
                        Where(mi => mi.Name == "CreateApplicantList").First();
                    applicantListMethod.Invoke(ac, new object[] { });
                }
            }
            else
            {
                if (popups.Any())
                {
                    popups.Last().reason = "Hiring " + pcm.name;
                }
            }
        }

        void OnTechResearched(GameEvents.HostTargetAction<RDTech, RDTech.OperationResult> hta)
        {
            if (hta.target == RDTech.OperationResult.Successful)
            {
                // TODO - check for over science

                if (popups.Any())
                {
                    Popup last = popups.Last();
                    if (last.transactionReason == TransactionReasons.RnDTechResearch && last.isDelta)
                    {
                        last.anchorType = AnchorType.Transform;
                        last.referencePosition = hta.host.transform;
                    }
                }

                AddPopup(Currency.Science, -hta.host.scienceCost, TransactionReasons.RnDTechResearch, "Research", hta.host.transform, false);
            }
        }

        void OnCurrencyModified(CurrencyModifierQuery qry)
        {
            if (qry.reason == TransactionReasons.StructureConstruction)
            {
                AddFacilityPopup(Currency.Funds, qry.GetInput(Currency.Funds), qry.reason, "Upgrade", false);
            }
            else if (qry.reason == TransactionReasons.StructureRepair)
            {
                AddFacilityPopup(Currency.Funds, qry.GetInput(Currency.Funds), qry.reason, "Repair", false);
            }
            else if (qry.reason == TransactionReasons.CrewRecruited)
            {
                // Crew recruitment happens in an odd order - first the base amount is taken off, then this is called,
                // *then* the delta is taken off.  However, because funds are clamped to zero, this can allow us to
                // not have the delta removed.  So track that case to correct for later.
                if (Funding.Instance.Funds < 0)
                {
                    crewCorrectionRequired = true;
                    correctionAmount = Funding.Instance.Funds - qry.GetInput(Currency.Funds) - qry.GetEffectDelta(Currency.Funds);
                }
                else
                {
                    crewCorrectionRequired = false;
                    AddPopup(Currency.Funds, qry.GetInput(Currency.Funds), qry.reason, "Hiring Kerbal", false);
                }
            }
            else if (qry.reason == TransactionReasons.ScienceTransmission)
            {
                AddPopup(Currency.Science, qry.GetInput(Currency.Science), qry.reason, "Science Transmission", false);
            }
        }

        private static string CurrencySymbol(Currency c)
        {
            // TODO - Fix these under KSP 1.1. when the font is hopefully available
            if (c == Currency.Funds)
            {
                //return "£";
                return "√";
            }
            else if (c == Currency.Reputation)
            {
                //return "¡";
                return "★";
            }
            else // Currency.Science
            {
                //return "©";
                return "⚛";
            }
        }

        private static Color CurrencyColor(Currency c, double amount)
        {
            if (c == Currency.Funds)
            {
                return amount >= 0.0 ? fundsColor : fundsNegativeColor;
            }
            else if (c == Currency.Reputation)
            {
                return amount >= 0.0 ? reputationColor : reputationNegativeColor;
            }
            else // Currency.Science
            {
                return amount >= 0.0 ? scienceColor : scienceNegativeColor;
            }
        }

        private static Color BackgroundColor(Color c)
        {
            // Calculate luminance
            double l =
                0.2126 * (c.r <= 0.03928 ? c.r / 12.92 : Math.Pow((c.r + 0.055) / 1.055, 2.4)) +
                0.7152 * (c.g <= 0.03928 ? c.g / 12.92 : Math.Pow((c.g + 0.055) / 1.055, 2.4)) +
                0.0722 * (c.b <= 0.03928 ? c.b / 12.92 : Math.Pow((c.b + 0.055) / 1.055, 2.4));

            return (l > 0.1158 ? Color.black : Color.gray);
        }
    }
}

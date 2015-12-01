﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using KSP;
using CompoundParts;
using Contracts;
using Strategies;
using Strategies.Effects;

namespace Strategia
{
    /// <summary>
    /// Strategy effect that improves vessel values.
    /// </summary>
    public class VesselValueImprover : StrategyEffect
    {
        enum Attribute
        {
            ISP,
            ParachuteDrag,
            StrutStrength,
        }

        string trait;
        List<float> multipliers;
        Attribute attribute;

        private static Dictionary<string, float> originalValues = new Dictionary<string, float>();
        private static Dictionary<Attribute, string> attributeTitles = new Dictionary<Attribute, string>();

        static VesselValueImprover()
        {
            attributeTitles[Attribute.ISP] = "Engine ISP";
            attributeTitles[Attribute.ParachuteDrag] = "Parachute effectiveness";
            attributeTitles[Attribute.StrutStrength] = "Strut strength";

        }

        public VesselValueImprover(Strategy parent)
            : base(parent)
        {
        }

        protected override string GetDescription()
        {
            float multiplier = Parent.GetLeveledListItem(multipliers);
            string multiplierStr = ToPercentage(multiplier, "F1");

            return attributeTitles[attribute] + " increased by " + multiplierStr + " when " + StringUtil.ATrait(trait) + " is on board.";
        }

        protected override void OnLoadFromConfig(ConfigNode node)
        {
            base.OnLoadFromConfig(node);
            multipliers = ConfigNodeUtil.ParseValue<List<float>>(node, "multiplier");
            trait = ConfigNodeUtil.ParseValue<string>(node, "trait");
            attribute = ConfigNodeUtil.ParseValue<Attribute>(node, "attribute");
        }

        protected override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
        }

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
        }

        protected override void OnRegister()
        {
            GameEvents.onVesselChange.Add(new EventData<Vessel>.OnEvent(OnVesselChange));
            GameEvents.onFlightReady.Add(new EventVoid.OnEvent(OnFlightReady));
        }

        protected override void OnUnregister()
        {
            GameEvents.onVesselChange.Remove(new EventData<Vessel>.OnEvent(OnVesselChange));
            GameEvents.onFlightReady.Remove(new EventVoid.OnEvent(OnFlightReady));
        }

        private void OnFlightReady()
        {
            Debug.Log("Strategia.VesselValueImprover.OnFlightReady");
            HandleVessel(FlightGlobals.ActiveVessel);
        }

        private void OnVesselChange(Vessel vessel)
        {
            Debug.Log("Strategia.VesselValueImprover.OnVesselChange");
            HandleVessel(vessel);
        }

        private void HandleVessel(Vessel vessel)
        {
            Debug.Log("Strategia.VesselValueImprover.HandleVessel");

            // Check for our trait
            bool needsIncrease = false;
            foreach (ProtoCrewMember pcm in VesselUtil.GetVesselCrew(vessel))
            {
                if (pcm.experienceTrait.Config.Name == trait)
                {
                    needsIncrease = true;
                    break;
                }
            }

            // Find all relvant parts
            foreach (Part p in vessel.parts)
            {
                foreach (PartModule m in p.Modules)
                {
                    switch (attribute)
                    {
                        case Attribute.ISP:
                            ModuleEngines engine = m as ModuleEngines;
                            if (engine != null)
                            {
                                Debug.Log("Got an engine in part " + p.partName);
                                FloatCurve curve = engine.atmosphereCurve;
                                ConfigNode node = new ConfigNode();
                                curve.Save(node);

                                // Find and adjust the vacuum ISP
                                ConfigNode newNode = new ConfigNode();
                                foreach (ConfigNode.Value pair in node.values)
                                {
                                    string[] values = pair.value.Split(new char[] { ' ' });
                                    if (values[0] == "0")
                                    {
                                        float value = float.Parse(values[1]);
                                        SetValue(p.partName, needsIncrease, ref value);
                                        values[1] = value.ToString("F1");
                                        newNode.AddValue(pair.name, string.Join(" ", values));
                                    }
                                    else
                                    {
                                        newNode.AddValue(pair.name, pair.value);
                                    }
                                    Debug.Log("    node data " + pair.name + " = " + pair.value);
                                }
                                curve.Load(newNode);
                            }
                            break;
                        case Attribute.ParachuteDrag:
                            ModuleParachute parachute = m as ModuleParachute;
                            if (parachute != null)
                            {
                                SetValue(p.partName, needsIncrease, ref parachute.fullyDeployedDrag);
                            }
                            break;
                        case Attribute.StrutStrength:
                            CModuleStrut strut = m as CModuleStrut;
                            if (strut != null)
                            {
                                SetValue(p.partName + "_linear", needsIncrease, ref strut.linearStrength);
                                SetValue(p.partName + "_angular", needsIncrease, ref strut.angularStrength);
                            }
                            break;
                    }
                }
            }
        }

        private void SetValue(string name, bool increaseRequired, ref float value)
        {
            // Multiplier to use
            float multiplier = Parent.GetLeveledListItem(multipliers);

            // Cache the original value
            if (!originalValues.ContainsKey(name))
            {
                originalValues[name] = value;
            }
            value = originalValues[name] * (increaseRequired ? multiplier : 1.0f);
        }
    }
}
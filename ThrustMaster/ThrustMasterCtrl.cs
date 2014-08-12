using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;

namespace ThrustMaster
{

    public  class ThrustMasterBase : MonoBehaviour
    {
        string LogString(params object[] list)
        {
            var sb = new StringBuilder();
            foreach (var k in list) sb.Append(k.ToString());
            return sb.ToString();
        }

        public void Log(params object[] list)
        {
            Debug.Log("[THRUSTMASTER] " + LogString(list));
        }
        public void Warning(params object[] list)
        {
            Debug.LogWarning("[THRUSTMASTER] " + LogString(list));
        }
        public void Error(params object[] list)
        {
            Debug.LogError("[THRUSTMASTER] " + LogString(list));
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class ThrustMasterCtrl : ThrustMasterBase
    {
        public void Awake()
        {
            Log("ThrustMasterCtrl awoken");
        }

        bool engaged = false;
        float target = 0;
        float actualTarget = 0;
        bool autoLand = false;

        float vesselMass = 0;
        float maxThrust = 0;
        float currentThrust = 0;
        float maxAcceleration = 0;
        float currentAcceleration = 0;

        float verticalSpeed = 0;

        float desiredAcceleration = 0;
        float error = 0;
        float compensationMultiplier = 10;
        float compensationPower = 3;
        float compensatedAcceleration = 0;
        float landingMul = 0.1f;

        float gravity = 0;
        float throttle = 0;

        Vessel wireVessel = null;

        void FlyByWire(FlightCtrlState fcs)
        {
            if (engaged)
            {
                fcs.mainThrottle = throttle;
            }
        }

        void FixedUpdate()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (!FlightGlobals.ready || !enabled) vessel = null;
            if (wireVessel != vessel)
            {
                if (wireVessel != null) wireVessel.OnFlyByWire -= FlyByWire;
                wireVessel = vessel;
                if (wireVessel != null) wireVessel.OnFlyByWire += FlyByWire;
            }
            if (vessel == null) return;

            // vessel mass
            vesselMass = vessel.GetTotalMass();
            if (vesselMass <= 0) return;

            // max thrust available
            maxThrust = 0f;
            currentThrust = 0f;
            //var engines = from part in vessel.GetActiveParts()
            //              from engine in part.Modules.OfType<ModuleEngines>()
            //              where engine.isEnabled && engine.EngineIgnited && !engine.getFlameoutState
            //              select engine;
            //foreach (var engine in engines)
            foreach (var part in vessel.GetActiveParts())
            {
                foreach (var engine in part.Modules.OfType<ModuleEngines>())
                {
                    if (engine.isEnabled && engine.EngineIgnited && !engine.getFlameoutState)
                    {
                        // average thrust vector
                        var dir = new Vector3();
                        foreach (var xform in engine.thrustTransforms) dir += -xform.forward;
                        dir /= engine.thrustTransforms.Count > 0 ? engine.thrustTransforms.Count : 1;

                        // efficiency in the up direction
                        var efficiency = Vector3.Dot(dir, vessel.GetTransform().up);

                        // tweakable thrust cap
                        var cap = engine.thrustPercentage * 0.01f;

                        // velocity curve penalty
                        var curve = engine.useVelocityCurve ? engine.velocityCurve.Evaluate(vessel.GetSrfVelocity().magnitude) : 1;

                        // max thrust delivered by this engine
                        var thrust = efficiency * cap * curve * engine.maxThrust;
                        maxThrust += thrust;

                        currentThrust += engine.currentThrottle * thrust;

                    }
                }
            }


            // convert to accel
            maxAcceleration = maxThrust / vesselMass;
            currentAcceleration = currentThrust / vesselMass;

            // figure out local gravity
            var altitude = vessel.orbit.altitude + vessel.orbit.referenceBody.Radius;
            gravity = (float) (vessel.orbit.referenceBody.gravParameter / (altitude * altitude));

            verticalSpeed = (float) vessel.verticalSpeed;

            actualTarget = target;
            if (autoLand)
            {
                var radarAltitude = (float)(vessel.altitude - vessel.terrainAltitude);
                actualTarget = -radarAltitude * landingMul + target;
            }
            var speedError = verticalSpeed - actualTarget;
            
            // set thrust to counteract local gravity + desired speed
            desiredAcceleration = gravity - speedError;
            error = currentAcceleration - desiredAcceleration;
            compensatedAcceleration = desiredAcceleration - Mathf.Pow(error * compensationMultiplier, compensationPower);
            throttle = Mathf.Clamp01(compensatedAcceleration / maxAcceleration);
        }

        List<float> buttons = new List<float> { 30f, 2.5f, 0f, -2.5f, -30f };

        bool debug = false;
        GUIStyle off = null;
        GUIStyle on = null;
        int handle = 48273682;
        Rect win = new Rect(100, 100, 10, 10);
        void OnGUI()
        {
            if (!FlightGlobals.ready) return;
            if (off == null)
            {
                off = new GUIStyle(GUI.skin.button);
                off.fixedWidth = 40;
                off.fixedHeight = 40;
            }
            if (on == null)
            {
                on = new GUIStyle(off);
                on.normal.textColor = Color.yellow;
                on.fontStyle = FontStyle.Bold;
            }
            win = GUILayout.Window(handle, win, Win, "ThrustMaster", GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
        }
        void Win(int id)
        {
            {
                GUILayout.BeginVertical();
                foreach (var f in buttons)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (f == 0f)
                    {
                        if (GUILayout.Button("OFF", off)) engaged = false;
                        if (GUILayout.Button("AUTO", engaged && autoLand ? on : off)) { engaged = true; autoLand = !autoLand; }
                    }
                    if (GUILayout.Button((f>0 ? "+" : "")+f.ToString("0.0"), engaged && target == f ? on : off)) { engaged = true; target = f; }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }

            if (GUILayout.Button("debug")) debug = !debug;
            if (debug)
            {
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("actual");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(actualTarget.ToString("0.000") + " m/s");
                    GUILayout.EndHorizontal();
                }
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("landingMul");
                    GUILayout.FlexibleSpace();
                    float.TryParse(GUILayout.TextField(landingMul.ToString("0.000")), out landingMul);
                    GUILayout.EndHorizontal();
                }
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("compensationMultiplier");
                    GUILayout.FlexibleSpace();
                    float.TryParse(GUILayout.TextField(compensationMultiplier.ToString("0.000")), out compensationMultiplier);
                    GUILayout.EndHorizontal();
                }
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("compensationPower");
                    GUILayout.FlexibleSpace();
                    float.TryParse(GUILayout.TextField(compensationPower.ToString("0.000")), out compensationPower);
                    GUILayout.EndHorizontal();
                }
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("verticalSpeed");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(verticalSpeed.ToString("0.000") + " m/s");
                    GUILayout.EndHorizontal();
                }
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("vesselMass");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(vesselMass.ToString("0.000") + " t");
                    GUILayout.EndHorizontal();
                }
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("maxThrust");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(maxThrust.ToString("0.000") + " kN");
                    GUILayout.EndHorizontal();
                }
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("currentThrust");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(currentThrust.ToString("0.000") + " kN");
                    GUILayout.EndHorizontal();
                }
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("maxAcceleration");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(maxAcceleration.ToString("0.000") + " m/s/s");
                    GUILayout.EndHorizontal();
                }
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("currentAcceleration");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(currentAcceleration.ToString("0.000") + " m/s/s");
                    GUILayout.EndHorizontal();
                }
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("gravity");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(gravity.ToString("0.000") + " m/s/s");
                    GUILayout.EndHorizontal();
                }

                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("desiredAcceleration");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(desiredAcceleration.ToString("0.000") + " m/s/s");
                    GUILayout.EndHorizontal();
                }
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("error");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(error.ToString("0.000") + " m/s/s");
                    GUILayout.EndHorizontal();
                }
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("compensatedAcceleration");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(compensatedAcceleration.ToString("0.000") + " m/s/s");
                    GUILayout.EndHorizontal();
                }

                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("throttle");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(throttle.ToString("0.000%"));
                    GUILayout.EndHorizontal();
                }

                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Engaged");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(engaged ? "Yes" : "No");
                    GUILayout.EndHorizontal();
                }
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Target");
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(target.ToString("0.00") + " m/s");
                    GUILayout.EndHorizontal();
                }
            }

            GUI.DragWindow();
        }
    }
}

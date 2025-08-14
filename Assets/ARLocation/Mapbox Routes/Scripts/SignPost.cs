using UnityEngine;
using TMPro;
using System.Collections;

namespace ARLocation.MapboxRoutes
{
    public class SignPost : AbstractRouteSignpost
    {
        // ================================================================================ //
        //  Public Classes                                                                  //
        // ================================================================================ //

        [System.Serializable]
        public enum StateType
        {
            Hidden,
            Following,
            Idle,
            Deactivated,
            MapPin,
        }

        [System.Serializable]
        public struct MachineState
        {
            public StateType Type;
            public bool HasArrow;
        }

        [System.Serializable]
        public class SignSettingsData
        {
            [Tooltip("The container GameObject for the road-sign that displays the route information to the user. If set to 'None' it won't be displayed.")]
            public Transform Container;

            [Tooltip("The label that displays the maneuver instructions to the user. Should be a child of the road-sign container.")]
            public TMP_Text DirectionLabel;

            [Tooltip("The label that displays the distance from the user's current location to the next target. Should be a child of the road-sign container.")]
            public TMP_Text DistanceLabel;

            [Tooltip("The height of the road-sign.")]
            public float Height = 2;

            [Tooltip("The road-sign always follows ahead of the user at some distance, being placed in a straight line form the user's current position to the next target position."
                    + " This is settting defines the distance that the road-sign will keep ahead from the user.")]
            public float FollowDistance = 10;
        }

        [System.Serializable]
        public class ArrowSettingsData
        {
            [Tooltip("The container GameObject for the 3D arrow that indicates the maneuver direction to the user. If set to 'None' it won't be displayed.")]
            public Transform Container;

            [Tooltip("The distance at which the 3D arrow drops/appears. Should be euqal or less than the road-sign FollowDistance, and larger or equal than the DeactivationDistance.")]
            public float DropDistance = 5.0f;

            [Tooltip("The duration of the drop animation for the 3D arrow. If '0' there is no drop animation.")]
            public float DropDuration = 2.0f;

            [Tooltip("If true, the arrow will be hidden after the target has been deactivated and the next target becomes active.")]
            public bool HideArrowOnNextTarget = false;
        }

        [System.Serializable]
        public class MapPinSettingsData
        {
            [Tooltip("The container GameObject for the 3D model which will appear at the end of the route, indicating to the user that he has reached his destination. If set to 'None' it won't be displayed.")]
            public Transform Container;

            [Tooltip("A position offset that will be applied to the Container, relative to the target's position.")]
            public Vector3 Offset = new Vector3(0, 0, 0);

            [Tooltip("The duration of the drop animation for the finish sign. If '0' there is no drop animation.")]
            public float DropDuration = 1.5f;

            [Tooltip("The height from which the finish sign is dropped in the drop animation.")]
            public float DropHeight = 50.0f;
        }

        [System.Serializable]
        public class VoiceSettingsData
        {
            [Tooltip("Enable voice instructions for navigation")]
            public bool EnableVoiceInstructions = true;

            [Tooltip("Distance at which to announce the next instruction")]
            public float AnnouncementDistance = 50.0f;

            [Tooltip("Distance at which to give immediate instruction (e.g., 'Turn right now')")]
            public float ImmediateInstructionDistance = 10.0f;

            [Tooltip("Minimum time between voice announcements to avoid spam")]
            public float MinTimeBetweenAnnouncements = 5.0f;

            [Tooltip("Announce distance updates periodically")]
            public bool AnnounceDistanceUpdates = true;

            [Tooltip("Interval for distance announcements (in seconds)")]
            public float DistanceAnnouncementInterval = 30.0f;

            [Tooltip("Distance intervals for announcements (e.g., every 100m, 50m, etc.)")]
            public float[] DistanceThresholds = { 100f, 50f, 20f };
        }

        [System.Serializable]
        public class SettingsData
        {
            [Tooltip("The distance at which the target is deactivated and the next target is activated. Should be smaller than the sign-post FollowDistance and the direction arrow DropDistance.")]
            public float DeactivationDistance = 10.0f;
        }

        // ================================================================================ //
        //  Public Properties                                                               //
        // ================================================================================ //

        [Tooltip("Settings related to the road-sign that shows the user routing information.")]
        public SignSettingsData RoadSignSettings;

        [Tooltip("Settings related to the 3D arrow that indicates maneuver directions to the user.")]
        public ArrowSettingsData DirectionArrowSettings;

        [Tooltip("Settings related to 3D model that appears on the end of the route, indicating to the user that he has arrived at his destination.")]
        public MapPinSettingsData FinishSignSettings;

        [Tooltip("Settings related to voice instructions.")]
        public VoiceSettingsData VoiceSettings;

        public SettingsData OtherSettings;

        [Tooltip("Reference to the TTS Manager for voice instructions")]
        public TTSManager ttsManager;

        // ================================================================================ //
        //  Private Classes                                                                 //
        // ================================================================================ //

        [System.Serializable]
        private class InputData
        {
            public bool IsCurrentTarget;
            public bool IsLast;
            public float Distance;
        }

        [System.Serializable]
        private class VoiceState
        {
            public float LastAnnouncementTime;
            public float LastDistanceAnnouncementTime;
            public bool HasAnnouncedInstruction;
            public bool HasAnnouncedImmediate;
            public string LastInstruction;
            public float[] LastDistanceThresholds;

            public VoiceState()
            {
                LastAnnouncementTime = -1f;
                LastDistanceAnnouncementTime = -1f;
                HasAnnouncedInstruction = false;
                HasAnnouncedImmediate = false;
                LastInstruction = "";
            }
        }

        // ================================================================================ //
        //  Private Fields                                                                  //
        // ================================================================================ //

        private InputData input = new InputData();
        private MachineState state = new MachineState { Type = StateType.Hidden, HasArrow = false };
        private VoiceState voiceState = new VoiceState();
        private float arrowTime = 0;
        private float mapPinTime = 0;

        private float L0 => RoadSignSettings.FollowDistance;
        private float L1 => DirectionArrowSettings.DropDistance;
        private float L2 => OtherSettings.DeactivationDistance;

        private bool v0 => input.IsCurrentTarget && (input.Distance >= L0);
        private bool v1 => input.IsCurrentTarget && (input.Distance < L0) && (input.Distance >= L1);
        private bool v2 => input.IsCurrentTarget && (input.Distance < L1) && (input.Distance >= L2);
        private bool v3 => input.IsCurrentTarget && (input.Distance < L2);
        private bool v4 => !input.IsCurrentTarget && (input.Distance <= L2);
        private bool v5 => !input.IsCurrentTarget && (input.Distance > L2);

        private Transform arrowContainer => DirectionArrowSettings.Container;
        private Transform signContainer => RoadSignSettings.Container;
        private Transform mapPinContainer => FinishSignSettings.Container;

        // ================================================================================ //
        //  Monobehaviour Methods                                                           //
        // ================================================================================ //

        void Start()
        {
            // Initialize TTS Manager if not assigned
            if (ttsManager == null)
            {
                ttsManager = FindObjectOfType<TTSManager>();
                if (ttsManager == null)
                {
                    GameObject ttsGO = new GameObject("TTSManager");
                    ttsManager = ttsGO.AddComponent<TTSManager>();
                }
            }

            // Initialize distance thresholds tracking
            if (VoiceSettings.DistanceThresholds != null)
            {
                voiceState.LastDistanceThresholds = new float[VoiceSettings.DistanceThresholds.Length];
                for (int i = 0; i < voiceState.LastDistanceThresholds.Length; i++)
                {
                    voiceState.LastDistanceThresholds[i] = float.MaxValue;
                }
            }
        }

        void OnValidate()
        {
            if (L1 > L0)
            {
                DirectionArrowSettings.DropDistance = RoadSignSettings.FollowDistance;
            }

            if (L2 > L1)
            {
                OtherSettings.DeactivationDistance = DirectionArrowSettings.DropDistance;
            }
        }

        // ================================================================================ //
        //  AbstractSignPost Methods                                                        //
        // ================================================================================ //

        public override void Init(MapboxRoute route)
        {
            state = new MachineState { Type = StateType.Hidden, HasArrow = false };
            voiceState = new VoiceState();
            gameObject.SetActive(false);
        }

        public override void OffCurrentTarget(SignPostEventArgs args) 
        {
            // Reset voice state when leaving a target
            voiceState.HasAnnouncedInstruction = false;
            voiceState.HasAnnouncedImmediate = false;
        }

        public override void OnCurrentTarget(SignPostEventArgs args) 
        {
            // Reset voice state when entering a new target
            voiceState.HasAnnouncedInstruction = false;
            voiceState.HasAnnouncedImmediate = false;
            voiceState.LastInstruction = "";

            // Reset distance thresholds
            if (voiceState.LastDistanceThresholds != null)
            {
                for (int i = 0; i < voiceState.LastDistanceThresholds.Length; i++)
                {
                    voiceState.LastDistanceThresholds[i] = float.MaxValue;
                }
            }
        }

        public override bool UpdateSignPost(SignPostEventArgs args)
        {
            input.IsCurrentTarget = args.IsCurrentTarget;
            input.Distance = args.Distance;
            input.IsLast = args.StepIndex == (args.Route.NumberOfSteps - 1);

            var result = step();
            update(args);
            
            // Handle voice instructions
            if (VoiceSettings.EnableVoiceInstructions && input.IsCurrentTarget)
            {
                HandleVoiceInstructions(args);
            }

            return result;
        }

        // ================================================================================ //
        //  Private Methods                                                                 //
        // ================================================================================ //

        private void HandleVoiceInstructions(SignPostEventArgs args)
        {
            float currentTime = Time.time;
            float timeSinceLastAnnouncement = currentTime - voiceState.LastAnnouncementTime;

            // Avoid spam announcements
            if (timeSinceLastAnnouncement < VoiceSettings.MinTimeBetweenAnnouncements)
                return;

            string instruction = args.Instruction;
            if (string.IsNullOrEmpty(instruction))
                return;

            // Announce instruction at specified distance
            if (!voiceState.HasAnnouncedInstruction && 
                input.Distance <= VoiceSettings.AnnouncementDistance && 
                input.Distance > VoiceSettings.ImmediateInstructionDistance)
            {
                string announcement = $"In {Mathf.RoundToInt(input.Distance)} meters, {instruction}";
                StartCoroutine(SpeakInstruction(announcement));
                voiceState.HasAnnouncedInstruction = true;
                voiceState.LastInstruction = instruction;
            }
            // Immediate instruction
            else if (!voiceState.HasAnnouncedImmediate && 
                     input.Distance <= VoiceSettings.ImmediateInstructionDistance)
            {
                string announcement = input.IsLast ? "You have arrived at your destination" : instruction;
                StartCoroutine(SpeakInstruction(announcement));
                voiceState.HasAnnouncedImmediate = true;
            }

            // Distance threshold announcements
            HandleDistanceThresholds(args);

            // Periodic distance updates
            HandlePeriodicDistanceUpdates(args);
        }

        private void HandleDistanceThresholds(SignPostEventArgs args)
        {
            if (VoiceSettings.DistanceThresholds == null || voiceState.LastDistanceThresholds == null)
                return;

            for (int i = 0; i < VoiceSettings.DistanceThresholds.Length; i++)
            {
                float threshold = VoiceSettings.DistanceThresholds[i];
                
                if (input.Distance <= threshold && voiceState.LastDistanceThresholds[i] > threshold)
                {
                    string announcement = $"{Mathf.RoundToInt(input.Distance)} meters remaining";
                    StartCoroutine(SpeakInstruction(announcement));
                    voiceState.LastDistanceThresholds[i] = input.Distance;
                    break; // Only announce one threshold at a time
                }
            }
        }

        private void HandlePeriodicDistanceUpdates(SignPostEventArgs args)
        {
            if (!VoiceSettings.AnnounceDistanceUpdates)
                return;

            float currentTime = Time.time;
            float timeSinceLastDistanceUpdate = currentTime - voiceState.LastDistanceAnnouncementTime;

            if (timeSinceLastDistanceUpdate >= VoiceSettings.DistanceAnnouncementInterval)
            {
                if (input.Distance > VoiceSettings.ImmediateInstructionDistance)
                {
                    string announcement = $"Continue for {Mathf.RoundToInt(input.Distance)} meters";
                    StartCoroutine(SpeakInstruction(announcement));
                    voiceState.LastDistanceAnnouncementTime = currentTime;
                }
            }
        }

        private IEnumerator SpeakInstruction(string text)
        {
            if (ttsManager != null)
            {
                voiceState.LastAnnouncementTime = Time.time;
                yield return StartCoroutine(ttsManager.Speak(text));
                Debug.Log($"Voice Instruction: {text}");
            }
        }

        // Add method to manually trigger voice instruction (useful for testing or manual control)
        public void SpeakCustomInstruction(string instruction)
        {
            if (VoiceSettings.EnableVoiceInstructions)
            {
                StartCoroutine(SpeakInstruction(instruction));
            }
        }

        // Method to toggle voice instructions on/off
        public void ToggleVoiceInstructions()
        {
            VoiceSettings.EnableVoiceInstructions = !VoiceSettings.EnableVoiceInstructions;
            
            string status = VoiceSettings.EnableVoiceInstructions ? "enabled" : "disabled";
            StartCoroutine(SpeakInstruction($"Voice instructions {status}"));
        }

        private bool step()
        {
            bool result = !v3;

            if (v0)
            {
                setState(StateType.Following, false);
            }
            else if (v1)
            {
                setState(StateType.Idle, false);
            }
            else if (v2)
            {
                if (input.IsLast)
                {
                    setState(StateType.MapPin, false);
                }
                else
                {
                    setState(StateType.Idle, true);
                }
            }
            else if (v3 | v4 && !input.IsLast)
            {
                setState(StateType.Deactivated, !DirectionArrowSettings.HideArrowOnNextTarget);
            }
            else if (!input.IsLast)
            {
                setState(StateType.Hidden, false);
            }

            return result;
        }

        private void setState(StateType type, bool HasArrow)
        {
            setState(new MachineState { Type = type, HasArrow = HasArrow });
        }

        private void setState(MachineState next)
        {
            var ArrowContainer = DirectionArrowSettings.Container;
            var MapPinContainer = FinishSignSettings.Container;

            // Hidden -> Not Hidden transitions
            if (state.Type == StateType.Hidden && next.Type != StateType.Hidden)
            {
                gameObject.SetActive(true);
                ArrowContainer?.gameObject.SetActive(false);
                MapPinContainer?.gameObject.SetActive(false);
            }
            else if (state.Type != StateType.Hidden && next.Type == StateType.Hidden)
            {
                gameObject.SetActive(false);
                ArrowContainer?.gameObject.SetActive(false);
                MapPinContainer?.gameObject.SetActive(false);
            }

            // When transitioning from a state without arrow to a state with arrow...
            if (next.HasArrow && !state.HasArrow)
            {
                ArrowContainer?.gameObject.SetActive(true);
                arrowTime = 0;
            }
            // The opposite case...
            else if (!next.HasArrow && state.HasArrow)
            {
                ArrowContainer?.gameObject.SetActive(false);
            }

            // Map pin transition
            if (next.Type == StateType.MapPin && state.Type != StateType.MapPin)
            {
                MapPinContainer?.gameObject.SetActive(true);
                mapPinTime = 0;
            }
            else if (next.Type != StateType.MapPin && state.Type == StateType.MapPin)
            {
                MapPinContainer?.gameObject.SetActive(false);
            }

            state = next;
        }

        private void update(SignPostEventArgs args)
        {
            var SignContainer = RoadSignSettings.Container;
            var ArrowContainer = DirectionArrowSettings.Container;
            var MapPinContainer = FinishSignSettings.Container;
            var groundHeight = args.Route.Settings.GroundHeight;

            var relative = args.TargetPos - args.UserPos;
            relative.y = 0;
            var dir = relative.normalized;

            transform.position = args.TargetPos;
            Utils.Misc.SetTransformPositionY(transform, 0);

            if (ArrowContainer != null && ArrowContainer.gameObject.activeSelf)
            {
                if (args.StepIndex == args.Route.NumberOfSteps - 1)
                {
                    ArrowContainer.gameObject.SetActive(false);
                }

                float amp = 0.2f;
                var dropY = SignContainer == null ? RoadSignSettings.Height : SignContainer.transform.position.y;
                DropAndFloatUpdate(ArrowContainer.transform, arrowTime, dropY, Camera.main.transform.position.y, 2.0f, amp, 0.3f, 0.1f, 0.02f);
                arrowTime += Time.deltaTime;

                // Point it to the next target, if there is one
                if (args.NextTargetPos is Vector3 nextTargetPos)
                {
                    var lookAtPos = MathUtils.SetY(nextTargetPos, ArrowContainer.transform.position.y);
                    ArrowContainer.transform.LookAt(lookAtPos, Vector3.up);
                }
                else
                {
                    ArrowContainer.gameObject.SetActive(false);
                }
            }

            switch (state.Type)
            {
                case StateType.Hidden:
                    break;

                case StateType.Following:
                    if (SignContainer != null)
                    {
                        SignContainer.transform.position = MathUtils.SetY(args.UserPos, 0) + L0 * dir;
                        SignContainer.transform.forward = dir;
                    }
                    break;

                case StateType.Idle:
                    break;

                case StateType.MapPin:
                    if (MapPinContainer != null)
                    {
                        if (SignContainer != null)
                        {
                            var pos = SignContainer.transform.localToWorldMatrix.MultiplyPoint(FinishSignSettings.Offset);
                            MapPinContainer.transform.position = pos;
                        }
                        else
                        {
                            MapPinContainer.transform.position = args.TargetPos;
                        }

                        float amp = 1.0f;
                        DropAndFloatUpdate(MapPinContainer.transform, mapPinTime, 50, -groundHeight + amp, 1.5f, amp, 0.3f, 0.1f, 0.02f);

                        mapPinTime += Time.deltaTime;
                    }
                    break;
            }

            if (SignContainer != null)
            {
                Utils.Misc.SetTransformPositionY(SignContainer.transform, Camera.main.transform.position.y + RoadSignSettings.Height);
            }

            if (state.Type != StateType.Hidden)
            {
                if (RoadSignSettings.DistanceLabel != null)
                {
                    RoadSignSettings.DistanceLabel.text = $"{args.Distance.ToString("0")} m";
                }

                if (RoadSignSettings.DirectionLabel != null)
                {
                    RoadSignSettings.DirectionLabel.text = args.Instruction;
                }
            }
        }

        public static float EaseOutCubic(float start, float end, float value)
        {
            value--;
            end -= start;
            return end * (value * value * value + 1) + start;
        }

        public static void DropAndFloatUpdate(
                Transform transform,
                float time,
                float StartY,
                float EndY,
                float DropDuration,
                float Amplitude,
                float Frequency,
                float LfoAmp,
                float LfoFreq
        )
        {
            bool isDownwards = EndY < StartY;
            if (time < DropDuration)
            {
                float y = EaseOutCubic(StartY, EndY - Amplitude / 2, time / DropDuration);
                transform.position = MathUtils.SetY(transform.position, y);
            }
            else
            {
                float t = time - DropDuration;

                // Add a "wavy" floating movement
                float lfo = LfoAmp * Mathf.Sin(2 * Mathf.PI * LfoFreq * t);
                float phase = isDownwards ? 1.5f * Mathf.PI : 0;
                float dy = (Amplitude + lfo) * Mathf.Sin(t * 2 * Mathf.PI * Frequency + phase);

                transform.position = MathUtils.SetY(transform.position, EndY - Amplitude / 2 + (Amplitude + lfo) + dy);
            }
        }
    }
}
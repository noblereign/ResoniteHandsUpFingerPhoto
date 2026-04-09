using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using Renderite.Shared;
using ResoniteModLoader;

namespace HandsUpFingerPhoto;
public class HandsUpFingerPhoto : ResoniteMod {
	internal const string VERSION_CONSTANT = "1.0.0";
	public override string Name => "HandsUpFingerPhoto";
	public override string Author => "Noble";
	public override string Version => VERSION_CONSTANT;
	public override string Link => "https://github.com/noblereign/ResoniteHandsUpFingerPhoto/";

	public static ModConfiguration config;

	[AutoRegisterConfigKey]
	private static ModConfigurationKey<bool> ENABLED = new ModConfigurationKey<bool>("Enabled", "Enables the mod! Disabling will return you to the vanilla gesture.", () => true);

	[Range(0, 1)]
	[AutoRegisterConfigKey]
	private static ModConfigurationKey<float> ANGLE_THRESHOLD = new ModConfigurationKey<float>("Angle threshold", "Adjusts how parallel your hands have to be to keep the camera active. Higher is stricter!", () => 0.5f);

	[AutoRegisterConfigKey]
	private static ModConfigurationKey<float> DISTANCE_THRESHOLD = new ModConfigurationKey<float>("Starting distance", "Adjust how close your hands have to be to your face to begin a finger photo.", () => 0.275f);

	[AutoRegisterConfigKey]
	private static ModConfigurationKey<float> DEPTH_OFFSET = new ModConfigurationKey<float>("Depth offset", "Adjusts how close the camera preview will be to your face.", () => 0f);

	[AutoRegisterConfigKey]
	private static ModConfigurationKey<float> HEIGHT_OFFSET = new ModConfigurationKey<float>("Height offset", "Adjusts the height that the preview will be placed at.", () => 0f);

	[AutoRegisterConfigKey]
	private static ModConfigurationKey<bool> REQUIRE_TRIGGER_PRESS = new ModConfigurationKey<bool>("Use trigger press to open", "When enabled, you will need to click both triggers at the same time to bring up the camera preview. This can help prevent accidental finger photos.", () => false);

	[AutoRegisterConfigKey]
	private static ModConfigurationKey<bool> ALLOW_DEFAULT_GESTURE = new ModConfigurationKey<bool>("Allow vanilla gesture", "By default, the vanilla finger photo gesture will be disabled in favor of the modded one. Enable this setting to allow either of them to start a finger photo.", () => false);

	public override void OnEngineInit() {
		config = GetConfiguration();
		config.Save(true);

		Harmony harmony = new("dog.glacier.HandsUpFingerPhoto");
		harmony.PatchAll();
	}

	public static float HandToHandMaxDist = 0.38f;
	public static float HandToHandMinDist = 0.1f;

	public static float BakedForwardOffset = 0.05f;
	public static float BakedUpwardsOffset = 0.05f;

	public static float distanceLeeway = 0.04f;

	public static bool triggersReleasedDuringPose = false;
	public static bool isGestureActive = false;

	[HarmonyPatch(typeof(PhotoCaptureManager), "GetFingerGestureData")]
	class PhotoCaptureManager_GetFingerGestureData_Patch {
		static bool Prefix(PhotoCaptureManager __instance, Hand left, Hand right, ref PhotoCaptureManager.GestureData? __result) {
			if (__instance.IsUnderLocalUser && __instance.InputInterface.VR_Active && __instance.World.Focus != FrooxEngine.World.WorldFocus.Background && config.GetValue(ENABLED)) {
				float AngleThreshold = config.GetValue(ANGLE_THRESHOLD);
				float DistanceThreshold = config.GetValue(DISTANCE_THRESHOLD);

				float userScale = __instance.LocalUserRoot.GlobalScale;
				Slot HeadSlot = __instance.LocalUserRoot.HeadSlot;
				Slot RightHandSlot = __instance.LocalUserRoot.RightHandSlot;
				Slot LeftHandSlot = __instance.LocalUserRoot.LeftHandSlot;

				float3 headUp = HeadSlot.Up;
				float3 headRight = HeadSlot.Right;

				bool rightWristAngle1 = MathX.Dot(headUp, RightHandSlot.Forward) >= AngleThreshold;
				bool leftWristAngle1 = MathX.Dot(headUp, LeftHandSlot.Forward) >= AngleThreshold;
				bool rightWristAngle2 = MathX.Dot(headRight, RightHandSlot.Up) >= AngleThreshold;
				bool leftWristAngle2 = MathX.Dot(headRight, LeftHandSlot.Down) >= AngleThreshold;

				float distHeadToRight = MathX.Distance(HeadSlot.GlobalPosition, RightHandSlot.GlobalPosition);
				float distHeadToLeft = MathX.Distance(HeadSlot.GlobalPosition, LeftHandSlot.GlobalPosition);
				float distHands = MathX.Distance(RightHandSlot.GlobalPosition, LeftHandSlot.GlobalPosition);

				bool closeToRight = distHeadToRight <= (__instance._fingerGestureCharge < 0.1f ? (userScale * DistanceThreshold) : (userScale * (__instance.MaxDistance + distanceLeeway)));
				bool closeToLeft = distHeadToLeft <= (__instance._fingerGestureCharge < 0.1f ? (userScale * DistanceThreshold) : (userScale * (__instance.MaxDistance + distanceLeeway)));
				bool handsClose = distHands <= (userScale * HandToHandMaxDist);
				bool handsNotColliding = distHands >= (userScale * HandToHandMinDist);

				bool isHoldingPose = rightWristAngle1 && leftWristAngle1 && rightWristAngle2 && leftWristAngle2 &&
								closeToRight && closeToLeft && handsClose && handsNotColliding;

				if (isHoldingPose) {
					bool requireTriggerPress = config.GetValue(REQUIRE_TRIGGER_PRESS);

					float leftTriggerPressed = __instance.InputInterface.leftController.ActionPrimary.PressedOrHeld ? 1 : 0;
					float rightTriggerPressed = __instance.InputInterface.rightController.ActionPrimary.PressedOrHeld ? 1 : 0;
					bool triggersPressed = (leftTriggerPressed == 1 && rightTriggerPressed == 1);

					if (requireTriggerPress && !isGestureActive) {
						if (triggersPressed) {
							isGestureActive = true;
						} else {
							__result = null;
							return config.GetValue(ALLOW_DEFAULT_GESTURE);
						}
					}

					if (leftTriggerPressed == 0 && rightTriggerPressed == 0) {
						triggersReleasedDuringPose = true;
					}

					PhotoCaptureManager.GestureData gesture = default(PhotoCaptureManager.GestureData);

					float3 directionBetweenHands = (RightHandSlot.GlobalPosition - LeftHandSlot.GlobalPosition).Normalized;
					float3 averageHandUp = ((RightHandSlot.Forward + LeftHandSlot.Forward) / 2f).Normalized;

					float3 gestureDirectionGlobal = MathX.Cross(directionBetweenHands, averageHandUp).Normalized;
					gesture.direction = __instance._previewRoot.Target.Parent.GlobalVectorToLocal(gestureDirectionGlobal);

					gesture.center = ((LeftHandSlot.GlobalPosition + RightHandSlot.GlobalPosition) * 0.5f) + (gestureDirectionGlobal * (BakedForwardOffset + config.GetValue(DEPTH_OFFSET))) + (averageHandUp * (BakedUpwardsOffset + config.GetValue(HEIGHT_OFFSET)));
					gesture.size = MathX.Distance(LeftHandSlot.GlobalPosition, RightHandSlot.GlobalPosition) * 0.85f;
					gesture.distance = MathX.Distance(HeadSlot.GlobalPosition, gesture.center) / __instance.LocalUserRoot.GlobalScale;

					gesture.leftCorner = __instance._timerRoot.Target.Parent.GlobalPointToLocal(LeftHandSlot.GlobalPosition + (averageHandUp * .2f));
					gesture.rightCorner = __instance._timerRoot.Target.Parent.GlobalPointToLocal(RightHandSlot.GlobalPosition - (averageHandUp * .2f));

					// the OG uses like finger curls for it or something but i cant be bothered so i'm binding them to primary instead
					// this honestly might be more reliable since it relies on a legit button press??? i know sometimes i've had issues with getting finger photos to trigger the vanilla way
					if (requireTriggerPress && !triggersReleasedDuringPose) {
						gesture.timerTrigger = 0;
						gesture.takeTrigger = 0;
					} else {
						gesture.timerTrigger = leftTriggerPressed;
						gesture.takeTrigger = rightTriggerPressed;
					}

					gesture.center = __instance._previewRoot.Target.Parent.GlobalPointToLocal(gesture.center);

					__result = gesture;
					return false;
				} else {
					triggersReleasedDuringPose = false;
					isGestureActive = false;
					__result = null;
				}

				return config.GetValue(ALLOW_DEFAULT_GESTURE);
			} else {
				return true;
			}
		}
	}
}

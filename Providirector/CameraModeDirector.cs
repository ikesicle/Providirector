using Rewired;
using UnityEngine;
using HG.BlendableTypes;
using RoR2;
using RoR2.CameraModes;

namespace Providirector;

public class CameraModeDirector : CameraModePlayerBasic
{
    private BlendableVector3 idealLocalCameraPosOverride = new Vector3(0f, 0f, -5f);
    public float cameraDistance
    {
        get { return idealLocalCameraPosOverride.value.z; }
        set { idealLocalCameraPosOverride = new Vector3(0f, 0f, value); }
    }

    public static CameraModeDirector director = new() { isSpectatorMode = false };

    public override void UpdateInternal(object rawInstanceData, in CameraModeContext context, out UpdateResult result)
    {
        // Literally copy-pasted from the base class with a few mods to allow for zooming
        InstanceData instanceData = (InstanceData)rawInstanceData;
        CameraRigController cameraRigController = context.cameraInfo.cameraRigController;
        CameraTargetParams targetParams = context.targetInfo.targetParams;
        float fov = context.cameraInfo.baseFov;
        Quaternion quaternion = context.cameraInfo.previousCameraState.rotation;
        Vector3 position = context.cameraInfo.previousCameraState.position;
        float num = cameraRigController.baseFov;
        instanceData.neutralFov = Mathf.SmoothDamp(instanceData.neutralFov, num, ref instanceData.neutralFovVelocity, 0.2f, float.PositiveInfinity, Time.deltaTime);
        CharacterCameraParamsData dest = CharacterCameraParamsData.basic;
        dest.fov = instanceData.neutralFov;
        dest.idealLocalCameraPos.value = idealLocalCameraPosOverride.value;
        if ((bool)targetParams)
        {
            BlendableFloat.Blend(in targetParams.currentCameraParamsData.pivotVerticalOffset, ref dest.pivotVerticalOffset, 1f);
        }
        instanceData.minPitch = dest.minPitch.value;
        instanceData.maxPitch = dest.maxPitch.value;
        float pitch = instanceData.pitchYaw.pitch;
        float yaw = instanceData.pitchYaw.yaw;
        //Providirector.PLog("{0} - {1}", pitch, yaw);
        pitch = Mathf.Clamp(pitch, instanceData.minPitch, instanceData.maxPitch);
        yaw = Mathf.Repeat(yaw, 360f);
        Vector3 targetPivotPosition = CalculateTargetPivotPosition(in context);
        if ((bool)context.targetInfo.target)
        {
            quaternion = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 direction = quaternion * dest.idealLocalCameraPos.value;
            float magnitude = direction.magnitude;
            // We removed the parabolic camera controls - The camera now moves uniformly around the player character.

            float num3 = cameraRigController.Raycast(new Ray(targetPivotPosition, direction), magnitude, dest.wallCushion.value - 0.01f);
            instanceData.currentCameraDistance = num3;
            instanceData.cameraDistanceVelocity = 0f;
            position = targetPivotPosition + direction.normalized * instanceData.currentCameraDistance;
        }
        result = new UpdateResult();
        result.cameraState.position = position;
        result.cameraState.rotation = quaternion;
        result.cameraState.fov = fov;
        result.showSprintParticles = context.targetInfo.isSprinting;
        result.firstPersonTarget = null;
        UpdateCrosshair(rawInstanceData, in context, in result.cameraState, in targetPivotPosition, out result.crosshairWorldPosition);
    }

    public override void CollectLookInputInternal(object rawInstanceData, in CameraModeContext context, out CollectLookInputResult output)
    {
        ref readonly ViewerInfo viewerInfo = ref context.viewerInfo;
        float scrollwheel = Input.mouseScrollDelta.y;
        cameraDistance = Mathf.Clamp(cameraDistance + scrollwheel, -100f, -2f);
        
        Player inputPlayer = viewerInfo.inputPlayer;
        UserProfile userProfile = viewerInfo.userProfile;
        InstanceData instanceData = (InstanceData)rawInstanceData;
		output.lookInput = Vector3.zero;
        Vector2 vector = new Vector2(inputPlayer.GetAxisRaw(2), inputPlayer.GetAxisRaw(3));
        Vector2 aimStickVector = new Vector2(inputPlayer.GetAxisRaw(16), inputPlayer.GetAxisRaw(17));
        ConditionalNegate(ref vector.x, userProfile.mouseLookInvertX);
        ConditionalNegate(ref vector.y, userProfile.mouseLookInvertY);
        ConditionalNegate(ref aimStickVector.x, userProfile.stickLookInvertX);
        ConditionalNegate(ref aimStickVector.y, userProfile.stickLookInvertY);
        PerformStickPostProcessing(instanceData, in context, ref aimStickVector);
        float mouseLookSensitivity = userProfile.mouseLookSensitivity;
        float num = userProfile.stickLookSensitivity * CameraRigController.aimStickGlobalScale.value * 45f;
        Vector2 vector2 = new Vector2(userProfile.mouseLookScaleX, userProfile.mouseLookScaleY);
        Vector2 vector3 = new Vector2(userProfile.stickLookScaleX, userProfile.stickLookScaleY);
        vector *= vector2 * mouseLookSensitivity;
        aimStickVector *= vector3 * num;
        aimStickVector *= Time.deltaTime;
        output.lookInput = vector + aimStickVector;
        static void ConditionalNegate(ref float value, bool condition)
		{
			value = (condition ? (0f - value) : value);
		}
    }

    public override void ApplyLookInputInternal(object rawInstanceData, in CameraModeContext context, in ApplyLookInputArgs input)
    {
        InstanceData instanceData = (InstanceData)rawInstanceData;
        ref readonly TargetInfo targetInfo = ref context.targetInfo;
        float minPitch = instanceData.minPitch;
        float maxPitch = instanceData.maxPitch;
        instanceData.pitchYaw.pitch = Mathf.Clamp(instanceData.pitchYaw.pitch - input.lookInput.y, minPitch, maxPitch);
        instanceData.pitchYaw.yaw += input.lookInput.x;
        //Providirector.PLog("{0} - {1} : {2} - {3}", input.lookInput.x, input.lookInput.y, instanceData.pitchYaw.pitch, instanceData.pitchYaw.yaw);
    }
}
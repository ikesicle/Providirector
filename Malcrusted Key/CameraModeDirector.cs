using Rewired;
using System;
using UnityEngine;
using HG.BlendableTypes;
using RoR2;
using RoR2.CameraModes;

namespace DacityP;

public class CameraModeDirector : CameraModePlayerBasic
{
    private BlendableVector3 idealLocalCameraPosOverride = new Vector3(0f, 0f, -5f);

    public float cameraDistance
    {
        get { return idealLocalCameraPosOverride.value.z; }
        set { idealLocalCameraPosOverride = new Vector3(0f, 0f, value); }
    }

    public static CameraModeDirector director = new CameraModeDirector { isSpectatorMode = false };

    public override void UpdateInternal(object rawInstanceData, in CameraModeContext context, out UpdateResult result)
    {
        // Literally copy-pasted from the base class with a few mods to allow for zooming
        InstanceData instanceData = (InstanceData)rawInstanceData;
        CameraRigController cameraRigController = context.cameraInfo.cameraRigController;
        CameraTargetParams targetParams = context.targetInfo.targetParams;
        float fov = context.cameraInfo.baseFov;
        Quaternion quaternion = context.cameraInfo.previousCameraState.rotation;
        Vector3 position = context.cameraInfo.previousCameraState.position;
        GameObject firstPersonTarget = null;
        float num = cameraRigController.baseFov;
        if (context.targetInfo.isSprinting)
        {
            num *= 1.3f;
        }
        instanceData.neutralFov = Mathf.SmoothDamp(instanceData.neutralFov, num, ref instanceData.neutralFovVelocity, 0.2f, float.PositiveInfinity, Time.deltaTime);
        CharacterCameraParamsData dest = CharacterCameraParamsData.basic;
        dest.fov = instanceData.neutralFov;
        dest.idealLocalCameraPos.value = idealLocalCameraPosOverride.value;
        Vector2 vector = Vector2.zero;
        if ((bool)targetParams)
        {
            CharacterCameraParamsData.Blend(in targetParams.currentCameraParamsData, ref dest, 0.5f);
            fov = dest.fov.value;
            vector = targetParams.recoil;
        }
        if (dest.isFirstPerson.value)
        {
            firstPersonTarget = context.targetInfo.target;
        }
        instanceData.minPitch = dest.minPitch.value;
        instanceData.maxPitch = dest.maxPitch.value;
        float pitch = instanceData.pitchYaw.pitch;
        float yaw = instanceData.pitchYaw.yaw;
        pitch += vector.y;
        yaw += vector.x;
        pitch = Mathf.Clamp(pitch, instanceData.minPitch, instanceData.maxPitch);
        yaw = Mathf.Repeat(yaw, 360f);
        Vector3 targetPivotPosition = CalculateTargetPivotPosition(in context);
        if ((bool)context.targetInfo.target)
        {
            quaternion = Quaternion.Euler(pitch, yaw, 0f);
            //Debug.LogFormat("idealLocalCameraPos: {0}", dest.idealLocalCameraPos.value);
            Vector3 direction = targetPivotPosition + quaternion * dest.idealLocalCameraPos.value - targetPivotPosition;
            float magnitude = direction.magnitude;
            // We removed the parabolic camera controls - The camera now moves uniformly around the player character.

            Ray ray = new Ray(targetPivotPosition, direction);
            float num3 = cameraRigController.Raycast(new Ray(targetPivotPosition, direction), magnitude, dest.wallCushion.value - 0.01f);
            //Debug.DrawRay(ray.origin, ray.direction * magnitude, Color.yellow, Time.deltaTime);
            //Debug.DrawRay(ray.origin, ray.direction * num3, Color.red, Time.deltaTime);
            //Debug.LogFormat("Raycast Result: {0} / {1} -- CCD: {2}, OVR: {3}", num3, cameraDistance, instanceData.currentCameraDistance, idealLocalCameraPosOverride.value);
            if (instanceData.currentCameraDistance >= num3)
            {
                instanceData.currentCameraDistance = num3;
                instanceData.cameraDistanceVelocity = 0f;
            }
            else
            {
                instanceData.currentCameraDistance = Mathf.SmoothDamp(instanceData.currentCameraDistance, num3, ref instanceData.cameraDistanceVelocity, 0.5f);
            }
            position = targetPivotPosition + direction.normalized * instanceData.currentCameraDistance;
        }
        result.cameraState.position = position;
        result.cameraState.rotation = quaternion;
        result.cameraState.fov = fov;
        result.showSprintParticles = context.targetInfo.isSprinting;
        result.firstPersonTarget = firstPersonTarget;
        UpdateCrosshair(rawInstanceData, in context, in result.cameraState, in targetPivotPosition, out result.crosshairWorldPosition);
    }

    public override void CollectLookInputInternal(object rawInstanceData, in CameraModeContext context, out CollectLookInputResult output)
    {
        ref readonly ViewerInfo viewerInfo = ref context.viewerInfo;
        Player inputPlayer = viewerInfo.inputPlayer;
        float scrollwheel = Input.mouseScrollDelta.y;
        cameraDistance = Mathf.Clamp(cameraDistance + scrollwheel, -100f, -2f);
        
        
        // Rudimentary implement but it'll work for now
        base.CollectLookInputInternal(rawInstanceData, context, out output);
    }

    public override void ApplyLookInputInternal(object rawInstanceData, in CameraModeContext context, in ApplyLookInputArgs input)
    {
        InstanceData instanceData = (InstanceData)rawInstanceData;
        ref readonly TargetInfo targetInfo = ref context.targetInfo;
        float minPitch = instanceData.minPitch;
        float maxPitch = instanceData.maxPitch;
        instanceData.pitchYaw.pitch = Mathf.Clamp(instanceData.pitchYaw.pitch - input.lookInput.y, minPitch, maxPitch);
        instanceData.pitchYaw.yaw += input.lookInput.x;
        if ((bool)targetInfo.networkedViewAngles && targetInfo.networkedViewAngles.hasEffectiveAuthority && targetInfo.networkUser)
            targetInfo.networkedViewAngles.viewAngles = instanceData.pitchYaw;
    }
}
﻿using System;
namespace Providirector;
#pragma warning disable IDE1006 // Naming Styles
public static class InputManager
{
    public struct ButtonState
    {
        public bool down;

        public bool wasDown;

        public bool hasPressBeenClaimed;

        public bool justReleased
        {
            get
            {
                if (!down)
                {
                    return wasDown;
                }
                return false;
            }
        }

        public bool justPressed
        {
            get
            {
                if (down)
                {
                    return !wasDown;
                }
                return false;
            }
        }

        public void PushState(bool newState)
        {
            hasPressBeenClaimed &= newState;
            wasDown = down;
            down = newState;
        }
    }

    public static ButtonState SwapPage;
    public static ButtonState Slot1;
    public static ButtonState Slot2;
    public static ButtonState Slot3;
    public static ButtonState Slot4;
    public static ButtonState Slot5;
    public static ButtonState Slot6;
    public static ButtonState DebugSpawn;
    public static ButtonState ToggleAffixCommon;
    public static ButtonState ToggleAffixRare;
    public static ButtonState NextTarget;
    public static ButtonState PrevTarget;
    public static ButtonState FocusTarget;
    public static ButtonState BoostTarget;
    public static ButtonState ToggleHUD;
}


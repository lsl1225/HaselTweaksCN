using System;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HaselTweaks.Structs;
using GearsetArray = HaselTweaks.Structs.RaptureGearsetModule.GearsetArray;
using GearsetFlag = HaselTweaks.Structs.RaptureGearsetModule.GearsetFlag;

namespace HaselTweaks.Tweaks;

public unsafe class CharacterClassSwitcher : Tweak
{
    public override string Name => "Character Class Switcher";
    public override string Description => "Clicking on a class/job in the character window finds the gearset with the highest item level and equips it.\nHold shift on crafters to open the original desynthesis window.";
    public Configuration Config => Plugin.Config.Tweaks.CharacterClassSwitcher;

    public class Configuration
    {
        [ConfigField(Label = "Disable Tooltips", OnChange = nameof(OnTooltipConfigChange))]
        public bool DisableTooltips = false;
    }

    /*
        83 FD 14         cmp     ebp, 14h
        48 8B 6C 24 ??   mov     rbp, [rsp+68h+arg_8]
        7D 69            jge     short loc_140EB06A1     <- replacing this with a jmp rel8
    
        completely skips the whole if () {...} block, by jumping regardless of cmp result
     */
    [Signature("83 FD 14 48 8B 6C 24 ?? 7D 69")]
    private IntPtr TooltipAddress { get; init; }
    private bool TooltipPatchApplied = false;

    public override void Enable()
    {
        ApplyTooltipPatch(Config.DisableTooltips);
    }

    public override void Disable()
    {
        ApplyTooltipPatch(false);
    }

    private void OnTooltipConfigChange()
    {
        ApplyTooltipPatch(Config.DisableTooltips);
    }

    private void ApplyTooltipPatch(bool enable)
    {
        if (enable && !TooltipPatchApplied)
        {
            Utils.MemoryReplaceRaw(TooltipAddress + 8, new byte[] { 0xEB }); // jmp rel8

            TooltipPatchApplied = true;
        }
        else if (!enable && TooltipPatchApplied)
        {
            Utils.MemoryReplaceRaw(TooltipAddress + 8, new byte[] { 0x7D }); // jge rel8

            TooltipPatchApplied = false;
        }
    }

    // AddonCharacterClass_OnSetup (vf46)
    [AutoHook, Signature("48 8B C4 48 89 58 10 48 89 70 18 48 89 78 20 55 41 54 41 55 41 56 41 57 48 8D 68 A1 48 81 EC ?? ?? ?? ?? 0F 29 70 C8 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 17 F3 0F 10 35 ?? ?? ?? ?? 45 33 C9 45 33 C0 F3 0F 11 74 24 ?? 0F 57 C9 48 8B F9 E8", DetourName = nameof(OnSetup))]
    private Hook<OnSetupDelegate> OnSetupHook { get; init; } = null!;
    private delegate IntPtr OnSetupDelegate(AddonCharacterClass* addon, int a2);

    // AddonCharacterClass_ReceiveEvent (vf2)
    [AutoHook, Signature("48 89 5C 24 ?? 57 48 83 EC 20 48 8B D9 4D 8B D1", DetourName = nameof(OnEvent))]
    private Hook<ReceiveEventDelegate> ReceiveEventHook { get; init; } = null!;
    private delegate IntPtr ReceiveEventDelegate(AddonCharacterClass* addon, AtkEventType eventType, int eventParam, AtkEvent* atkEvent, IntPtr a5);

    // AddonCharacterClass_OnUpdate (vf49)
    [AutoHook, Signature("4C 8B DC 53 55 56 57 41 55 41 56", DetourName = nameof(OnUpdate))]
    private Hook<OnUpdateDelegate> OnUpdateHook { get; init; } = null!;
    private delegate void OnUpdateDelegate(AddonCharacterClass* addon, NumberArrayData** numberArrayData, StringArrayData** stringArrayData);

    [Signature("48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 84 C0 74 08 48 8B CB E8 ?? ?? ?? ?? 48 8B B4 24 ?? ?? ?? ??", ScanType = ScanType.StaticAddress)]
    private IntPtr g_InputManager { get; init; }

    [Signature("E8 ?? ?? ?? ?? 88 44 24 28 44 0F B6 CE")]
    private InputManager_GetInputStatus_Delegate InputManager_GetInputStatus { get; init; } = null!;
    private delegate bool InputManager_GetInputStatus_Delegate(IntPtr inputManager, int a2);

    [Signature("E8 ?? ?? ?? ?? 0F BF 94 1F")]
    private PlaySoundEffectDelegate PlaySoundEffect { get; init; } = null!;
    private delegate void PlaySoundEffectDelegate(int id, IntPtr a2, IntPtr a3, byte a4);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
    private const int VK_SHIFT = 0x10;
    private static bool IsShiftDown => (GetKeyState(VK_SHIFT) & 0x8000) == 0x8000;

    private static bool IsCrafter(int id)
    {
        return id >= 20 && id <= 27;
    }

    private IntPtr OnSetup(AddonCharacterClass* addon, int a2)
    {
        var result = OnSetupHook!.Original(addon, a2);

        for (var i = 0; i < AddonCharacterClass.NUM_CLASSES; i++)
        {
            // skip crafters as they already have ButtonClick events
            if (IsCrafter(i)) continue;

            var node = addon->BaseComponentNodes[i];
            if (node == null) continue;

            var collisionNode = (AtkCollisionNode*)node->UldManager.RootNode;
            if (collisionNode == null) continue;

            collisionNode->AtkResNode.AddEvent(AtkEventType.MouseClick, (uint)i + 2, &addon->AtkUnitBase.AtkEventListener, &collisionNode->AtkResNode, false);
            collisionNode->AtkResNode.AddEvent(AtkEventType.InputReceived, (uint)i + 2, &addon->AtkUnitBase.AtkEventListener, &collisionNode->AtkResNode, false);
        }

        return result;
    }

    private void OnUpdate(AddonCharacterClass* addon, NumberArrayData** numberArrayData, StringArrayData** stringArrayData)
    {
        OnUpdateHook!.Original(addon, numberArrayData, stringArrayData);

        for (var i = 0; i < AddonCharacterClass.NUM_CLASSES; i++)
        {
            // skip crafters as they already have Cursor Pointer flags
            if (IsCrafter(i)) continue;

            var node = addon->BaseComponentNodes[i];
            if (node == null) continue;

            var collisionNode = (AtkCollisionNode*)node->UldManager.RootNode;
            if (collisionNode == null) continue;

            var imageNode = (AtkImageNode*)node->UldManager.SearchNodeById(4);
            if (imageNode == null) continue;

            // if job is unlocked, it has full alpha
            var isUnlocked = imageNode->AtkResNode.Color.A == 255;

            if (isUnlocked)
                collisionNode->AtkResNode.Flags_2 |= 1 << 20; // add Cursor Pointer flag
            else
                collisionNode->AtkResNode.Flags_2 &= ~(uint)(1 << 20); // remove Cursor Pointer flag
        }
    }

    private IntPtr OnEvent(AddonCharacterClass* addon, AtkEventType eventType, int eventParam, AtkEvent* atkEvent, IntPtr a5)
    {
        // skip events for tabs
        if (eventParam < 2)
            goto OriginalCode;

        var node = addon->BaseComponentNodes[eventParam - 2];
        if (node == null)
            goto OriginalCode;

        var ownerNode = node->OwnerNode;
        if (ownerNode == null)
            goto OriginalCode;

        var imageNode = (AtkImageNode*)node->UldManager.SearchNodeById(4);
        if (imageNode == null)
            goto OriginalCode;

        // if job is unlocked, it has full alpha
        var isUnlocked = imageNode->AtkResNode.Color.A == 255;
        if (!isUnlocked)
            goto OriginalCode;

        var isClick =
            (eventType == AtkEventType.MouseClick || eventType == AtkEventType.ButtonClick) ||
            (eventType == AtkEventType.InputReceived && InputManager_GetInputStatus(g_InputManager, 12)); // 'A' button on a Xbox 360 Controller

        if (IsCrafter(eventParam - 2))
        {
            // as far as i can see, any controller button other than 'A' doesn't send InputReceived/ButtonClick events on button nodes,
            // so i can't move this functionality to the 'X' button. anyway, i don't think it's a big problem, because
            // desynthesis levels are shown at the bottom of the window, too.

            if (isClick && !IsShiftDown)
            {
                SwitchClassJob(8 + (uint)eventParam - 22);
                return IntPtr.Zero;
            }
        }
        else
        {
            if (isClick)
            {
                var textureInfo = imageNode->PartsList->Parts[imageNode->PartId].UldAsset;
                if (textureInfo == null || textureInfo->AtkTexture.Resource == null)
                    return ReceiveEventHook!.Original(addon, eventType, eventParam, atkEvent, a5);

                var iconId = textureInfo->AtkTexture.Resource->IconID;
                if (iconId <= 62100)
                    return ReceiveEventHook!.Original(addon, eventType, eventParam, atkEvent, a5);

                // yes, you see correctly. the iconId is 62100 + ClassJob RowId :)
                var classJobId = iconId - 62100;

                SwitchClassJob((uint)classJobId);

                return IntPtr.Zero; // handled
            }
            else if (eventType == AtkEventType.MouseOver)
            {
                ownerNode->AtkResNode.AddBlue = 16;
                ownerNode->AtkResNode.AddGreen = 16;
                ownerNode->AtkResNode.AddRed = 16;
            }
            else if (eventType == AtkEventType.MouseOut)
            {
                ownerNode->AtkResNode.AddBlue = 0;
                ownerNode->AtkResNode.AddGreen = 0;
                ownerNode->AtkResNode.AddRed = 0;
            }
        }

OriginalCode:
        return ReceiveEventHook!.Original(addon, eventType, eventParam, atkEvent, a5);
    }

    private void SwitchClassJob(uint classJobId)
    {
        var gearsetModule = RaptureGearsetModule.Instance();
        if (gearsetModule == null) return;

        // loop through all gearsets and find the one matching classJobId with the highest avg itemlevel
        var selectedGearset = (Index: -1, ItemLevel: -1);
        for (var i = 0; i < GearsetArray.Length; i++)
        {
            var gearset = gearsetModule->Gearsets[i];
            if (!gearset->Flags.HasFlag(GearsetFlag.Exists)) continue;
            if (gearset->ClassJob != classJobId) continue;

            if (selectedGearset.ItemLevel < gearset->ItemLevel)
                selectedGearset = (i + 1, gearset->ItemLevel);
        }

        PlaySoundEffect(8, IntPtr.Zero, IntPtr.Zero, 0);

        if (selectedGearset.Index == -1)
        {
            // TODO: localize
            Service.Chat.PrintError($"Couldn't find a suitable gearset.");
            return;
        }

        Plugin.XivCommon.Functions.Chat.SendMessage("/gs change " + selectedGearset.Index);
    }
}

using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using HaselCommon.Attributes;
using HaselCommon.Extensions;
using HaselCommon.Services;
using HaselCommon.Services.CommandManager;
using HaselTweaks.Enums;
using Lumina.Excel.GeneratedSheets;
using BattleNpcSubKind = Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind;
using DalamudObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace HaselTweaks.Tweaks;

public sealed class CommandsConfiguration
{
    [BoolConfig]
    public bool EnableItemLinkCommand = true;

    [BoolConfig]
    public bool EnableWhatMountCommand = true;

    [BoolConfig]
    public bool EnableWhatBardingCommand = true;

    [BoolConfig]
    public bool EnableGlamourPlateCommand = true;
}

public sealed unsafe class Commands(
    Configuration PluginConfig,
    CommandManager CommandManager,
    TranslationManager TranslationManager,
    IChatGui ChatGui,
    ITargetManager TargetManager)
    : Tweak<CommandsConfiguration>(PluginConfig, TranslationManager)
{
    private CommandHandler? ItemLinkCommandHandler;
    private CommandHandler? WhatMountCommandCommandHandler;
    private CommandHandler? WhatBardingCommandCommandHandler;
    private CommandHandler? GlamourPlateCommandCommandHandler;

    public override void OnInitialize()
    {
        ItemLinkCommandHandler = CommandManager.Register(OnItemLinkCommand);
        WhatMountCommandCommandHandler = CommandManager.Register(OnWhatMountCommand);
        WhatBardingCommandCommandHandler = CommandManager.Register(OnWhatBardingCommand);
        GlamourPlateCommandCommandHandler = CommandManager.Register(OnGlamourPlateCommand);
    }

    public override void OnEnable()
    {
        UpdateCommands(true);
    }

    public override void OnDisable()
    {
        UpdateCommands(false);
    }

    public override void OnConfigChange(string fieldName)
    {
        UpdateCommands(Status == TweakStatus.Enabled);
    }

    private void UpdateCommands(bool enable)
    {
        ItemLinkCommandHandler?.SetEnabled(enable && Config.EnableItemLinkCommand);
        WhatMountCommandCommandHandler?.SetEnabled(enable && Config.EnableWhatMountCommand);
        WhatBardingCommandCommandHandler?.SetEnabled(enable && Config.EnableWhatBardingCommand);
        GlamourPlateCommandCommandHandler?.SetEnabled(enable && Config.EnableGlamourPlateCommand);
    }

    [CommandHandler("/itemlink", "Commands.Config.EnableItemLinkCommand.Description")]
    private void OnItemLinkCommand(string command, string arguments)
    {
        uint id;
        try
        {
            id = Convert.ToUInt32(arguments.Trim());
        }
        catch (Exception e)
        {
            ChatGui.PrintError(e.Message);
            return;
        }

        var item = GetRow<Item>(id);
        if (item == null)
        {
            ChatGui.PrintError(t("Commands.ItemLink.ItemNotFound", id));
            return;
        }

        var idStr = new SeStringBuilder()
            .AddUiForeground(id.ToString(), 1)
            .Build();

        var sb = new SeStringBuilder()
            .AddUiForeground("\uE078 ", 32)
            .Append(tSe("Commands.ItemLink.Item", idStr, SeString.CreateItemLink(id)));

        ChatGui.Print(new XivChatEntry
        {
            Message = sb.Build(),
            Type = XivChatType.Echo
        });
    }

    [CommandHandler("/whatmount", "Commands.Config.EnableWhatMountCommand.Description")]
    private void OnWhatMountCommand(string command, string arguments)
    {
        var target = (Character*)(TargetManager.Target?.Address ?? 0);
        if (target == null)
        {
            ChatGui.PrintError(t("Commands.NoTarget"));
            return;
        }

        if (target->GameObject.GetObjectKind() != ObjectKind.Pc)
        {
            ChatGui.PrintError(t("Commands.TargetIsNotAPlayer"));
            return;
        }

        if (target->Mount.MountId == 0)
        {
            ChatGui.PrintError(t("Commands.WhatMount.TargetNotMounted"));
            return;
        }

        var mount = GetRow<Mount>(target->Mount.MountId);
        if (mount == null)
        {
            ChatGui.PrintError(t("Commands.WhatMount.MountNotFound"));
            return;
        }

        var sb = new SeStringBuilder()
            .AddUiForeground("\uE078 ", 32);

        var name = new SeStringBuilder()
            .AddUiForeground(GetMountName(mount.RowId), 1)
            .Build();

        var itemAction = FindRow<ItemAction>(row => row?.Type == 1322 && row.Data[0] == mount.RowId);
        if (itemAction == null || itemAction.RowId == 0)
        {
            ChatGui.Print(new XivChatEntry
            {
                Message = sb
                    .Append(tSe("Commands.WhatMount.WithoutItem", name))
                    .Build(),
                Type = XivChatType.Echo
            });
            return;
        }

        var item = FindRow<Item>(row => row?.ItemAction.Row == itemAction!.RowId);
        if (item == null)
        {
            ChatGui.Print(new XivChatEntry
            {
                Message = sb
                    .Append(tSe("Commands.WhatMount.WithoutItem", name))
                    .Build(),
                Type = XivChatType.Echo
            });
            return;
        }

        sb.Append(tSe("Commands.WhatMount.WithItem", name, SeString.CreateItemLink(item.RowId, false, GetItemName(item.RowId))));

        ChatGui.Print(new XivChatEntry
        {
            Message = sb.Build(),
            Type = XivChatType.Echo
        });
    }

    [CommandHandler("/whatbarding", "Commands.Config.EnableWhatMountCommand.Description")]
    private void OnWhatBardingCommand(string command, string arguments)
    {
        var target = TargetManager.Target;
        if (target == null)
        {
            ChatGui.PrintError(t("Commands.NoTarget"));
            return;
        }

        if (target.ObjectKind != DalamudObjectKind.BattleNpc || target.SubKind != (byte)BattleNpcSubKind.Chocobo)
        {
            ChatGui.PrintError(t("Commands.TargetIsNotAChocobo"));
            return;
        }

        var targetCharacter = (Character*)target.Address;

        var topRow = FindRow<BuddyEquip>(row => row?.ModelTop == targetCharacter->DrawData.Head.Value);
        var bodyRow = FindRow<BuddyEquip>(row => row?.ModelBody == targetCharacter->DrawData.Top.Value);
        var legsRow = FindRow<BuddyEquip>(row => row?.ModelLegs == targetCharacter->DrawData.Feet.Value);

        var stain = GetRow<Stain>(targetCharacter->DrawData.Legs.Stain)!;
        var name = new SeStringBuilder()
            .AddUiForeground(targetCharacter->GameObject.NameString, 1)
            .Build();

        var sb = new SeStringBuilder()
            .AddUiForeground("\uE078 ", 32)
            .Append(tSe("Commands.WhatBarding.AppearanceOf", name))
            .Add(NewLinePayload.Payload)
            .AddText($"  {GetAddonText(4987)}: ")
            .Append(stain.Name.ToString().FirstCharToUpper())
            .Add(NewLinePayload.Payload)
            .AddText($"  {GetAddonText(4991)}: {topRow?.Name.ToDalamudString().ToString() ?? GetAddonText(4994)}")
            .Add(NewLinePayload.Payload)
            .AddText($"  {GetAddonText(4992)}: {bodyRow?.Name.ToDalamudString().ToString() ?? GetAddonText(4994)}")
            .Add(NewLinePayload.Payload)
            .AddText($"  {GetAddonText(4993)}: {legsRow?.Name.ToDalamudString().ToString() ?? GetAddonText(4994)}");

        ChatGui.Print(new XivChatEntry
        {
            Message = sb.Build(),
            Type = XivChatType.Echo
        });
    }

    [CommandHandler("/glamourplate", "Commands.Config.EnableGlamourPlateCommand.Description")]
    private void OnGlamourPlateCommand(string command, string arguments)
    {
        if (!byte.TryParse(arguments, out var glamourPlateId) || glamourPlateId == 0 || glamourPlateId > 20)
        {
            ChatGui.PrintError(t("Commands.InvalidArguments"));
            return;
        }

        var raptureGearsetModule = RaptureGearsetModule.Instance();
        if (!raptureGearsetModule->IsValidGearset(raptureGearsetModule->CurrentGearsetIndex))
        {
            ChatGui.PrintError(t("Commands.GlamourPlate.InvalidGearset"));
            return;
        }

        raptureGearsetModule->EquipGearset(raptureGearsetModule->CurrentGearsetIndex, glamourPlateId);
    }
}
